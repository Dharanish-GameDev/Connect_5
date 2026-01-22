using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

public class Connect5Piece : MonoBehaviourPun
{
    [Header("Drop Settings")]
    public float speed = 1200f; // UI units/sec (anchoredPosition units)
    [SerializeField] private float dropStartPadding = 120f;

    [Header("Glow (Pulse)")]
    [SerializeField] private Outline outline;
    [SerializeField] private Color glowColor = new Color(1f, 0.95f, 0.2f, 1f);

    [Tooltip("Min outline thickness (effectDistance) during pulse.")]
    [SerializeField] private float glowMin = 4f;

    [Tooltip("Max outline thickness (effectDistance) during pulse.")]
    [SerializeField] private float glowMax = 10f;

    [Tooltip("How fast the pulse goes (cycles per second).")]
    [SerializeField] private float pulseSpeed = 2.2f;

    [Tooltip("Optional: pulse the alpha slightly (0..1). Set 0 for no alpha pulse.")]
    [SerializeField] private float alphaPulseAmount = 0.25f;

    private Coroutine pulseRoutine;

    private RectTransform rectTransform;

    private Vector2 targetPosition;
    private bool hasTarget;

    // InstantiationData
    private float xDest;
    private float yDest;
    private int parentViewID;
    private int columnIndex = -1;
    private int rowIndex = -1;

    public int Row => rowIndex;
    public int Col => columnIndex;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        if (outline == null) outline = GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = false;
            outline.effectColor = glowColor;
            outline.effectDistance = new Vector2(glowMin, glowMin);
        }

        ReadInstantiationData();
        StartCoroutine(SetupRoutine());
    }

    private void OnDisable()
    {
        StopPulse();
    }

    private void ReadInstantiationData()
    {
        object[] data = photonView.InstantiationData;

        // Expected:
        // [0]=xDest, [1]=yDest, [2]=parentViewID, [3]=columnIndex, [4]=rowIndex
        if (data == null || data.Length < 5)
        {
            Debug.LogWarning("[Connect5Piece] Missing InstantiationData (need 5 values).");
            hasTarget = false;
            return;
        }

        xDest = ToFloat(data[0]);
        yDest = ToFloat(data[1]);
        targetPosition = new Vector2(xDest, yDest);

        parentViewID = ToInt(data[2]);
        columnIndex = ToInt(data[3]);
        rowIndex = ToInt(data[4]);

        hasTarget = true;
    }

    private IEnumerator SetupRoutine()
    {
        for (int i = 0; i < 10; i++)
        {
            if (TryParentToTarget()) break;
            yield return null;
        }

        if (hasTarget && rectTransform != null)
        {
            rectTransform.anchoredPosition = new Vector2(xDest, GetDropStartHeightLikeConnect5());
            transform.SetAsLastSibling();
        }

        if (GameManagerConnect5.instance != null && rowIndex >= 0 && columnIndex >= 0)
        {
            GameManagerConnect5.instance.RegisterPiece(rowIndex, columnIndex, this);
        }
    }

    private bool TryParentToTarget()
    {
        if (parentViewID != 0)
        {
            PhotonView parentPV = PhotonView.Find(parentViewID);
            if (parentPV != null)
            {
                RectTransform parentRT = parentPV.GetComponent<RectTransform>();
                if (parentRT != null)
                {
                    transform.SetParent(parentRT, false);
                    NormalizeLocalUI();
                    return true;
                }
            }
        }

        GameManagerConnect5 gm = GameManagerConnect5.instance;
        if (gm != null && gm.PiecesParent != null)
        {
            transform.SetParent(gm.PiecesParent, false);
            NormalizeLocalUI();
            return true;
        }

        return false;
    }

    private void NormalizeLocalUI()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.anchoredPosition3D = new Vector3(rectTransform.anchoredPosition.x, rectTransform.anchoredPosition.y, 0f);
    }

    private float GetDropStartHeightLikeConnect5()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            RectTransform canvasRT = canvas.transform as RectTransform;
            if (canvasRT != null)
                return (canvasRT.rect.height * 0.5f) + dropStartPadding;

            return (canvas.pixelRect.height * 0.5f) + dropStartPadding;
        }

        RectTransform parent = rectTransform.parent as RectTransform;
        if (parent != null) return (parent.rect.height * 0.5f) + dropStartPadding;

        return yDest + 600f;
    }

    private void Update()
    {
        if (!hasTarget || rectTransform == null) return;

        rectTransform.anchoredPosition = Vector2.MoveTowards(
            rectTransform.anchoredPosition,
            targetPosition,
            speed * Time.deltaTime
        );

        if ((rectTransform.anchoredPosition - targetPosition).sqrMagnitude <= 0.25f)
        {
            rectTransform.anchoredPosition = targetPosition;
            hasTarget = false;

            if (columnIndex >= 0 && GameManagerConnect5.instance != null)
            {
                GameManagerConnect5.instance.ClearColumnHighlightLocal(columnIndex);
            }
        }
    }

    // ✅ Called by GameManager when win happens
    public void SetGlow(bool on)
    {
        if (outline == null) outline = GetComponent<Outline>();
        if (outline == null) return;

        if (on)
        {
            outline.enabled = true;
            outline.effectColor = glowColor;

            if (pulseRoutine == null)
                pulseRoutine = StartCoroutine(PulseGlow());
        }
        else
        {
            StopPulse();
            outline.enabled = false;
        }
    }

    private void StopPulse()
    {
        if (pulseRoutine != null)
        {
            StopCoroutine(pulseRoutine);
            pulseRoutine = null;
        }

        if (outline != null)
        {
            outline.effectDistance = new Vector2(glowMin, glowMin);
            outline.effectColor = glowColor;
        }
    }

    private IEnumerator PulseGlow()
    {
        float t = 0f;
        while (true)
        {
            t += Time.unscaledDeltaTime * pulseSpeed;

            float s = (Mathf.Sin(t * Mathf.PI * 2f) + 1f) * 0.5f;

            float thickness = Mathf.Lerp(glowMin, glowMax, s);
            if (outline != null)
            {
                outline.effectDistance = new Vector2(thickness, thickness);

                if (alphaPulseAmount > 0f)
                {
                    Color c = glowColor;
                    c.a = Mathf.Clamp01(glowColor.a - alphaPulseAmount + (alphaPulseAmount * 2f * s));
                    outline.effectColor = c;
                }
            }

            yield return null;
        }
    }

    private float ToFloat(object obj)
    {
        if (obj is float f) return f;
        if (obj is double d) return (float)d;
        if (obj is int i) return i;
        if (obj is long l) return l;
        return 0f;
    }

    private int ToInt(object obj)
    {
        if (obj is int i) return i;
        if (obj is long l) return (int)l;
        if (obj is float f) return Mathf.RoundToInt(f);
        if (obj is double d) return Mathf.RoundToInt((float)d);
        return -1;
    }
}
