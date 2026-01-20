using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

#pragma warning disable 0618

namespace TMPro.Examples
{
    public class TMP_TextSelector_B : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler,
        IPointerUpHandler
    {
        public RectTransform TextPopup_Prefab_01;

        private RectTransform m_TextPopup_RectTransform;
        private TextMeshProUGUI m_TextPopup_TMPComponent;

        private TextMeshProUGUI m_TextMeshPro;
        private Canvas m_Canvas;
        private Camera m_Camera;

        private bool isHoveringObject;
        private int m_selectedWord = -1;
        private int m_selectedLink = -1;
        private int m_lastIndex = -1;

        private Matrix4x4 m_matrix;
        private TMP_MeshInfo[] m_cachedMeshInfoVertexData;

        private const string k_LinkText = "You have selected link <#ffff00>";

        void Awake()
        {
            m_TextMeshPro = GetComponent<TextMeshProUGUI>();
            if (m_TextMeshPro == null)
            {
                enabled = false;
                return;
            }

            m_Canvas = GetComponentInParent<Canvas>();
            m_Camera = m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : m_Canvas.worldCamera;

            // Force TMP to generate mesh (UNITY 2019 FIX)
            m_TextMeshPro.ForceMeshUpdate();
            m_cachedMeshInfoVertexData = m_TextMeshPro.textInfo.CopyMeshInfoVertexData();

            if (TextPopup_Prefab_01 != null)
            {
                m_TextPopup_RectTransform = Instantiate(TextPopup_Prefab_01, m_Canvas.transform);
                m_TextPopup_TMPComponent = m_TextPopup_RectTransform.GetComponentInChildren<TextMeshProUGUI>();
                m_TextPopup_RectTransform.gameObject.SetActive(false);
            }
        }

        void OnEnable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        }

        void OnTextChanged(Object obj)
        {
            if (obj == m_TextMeshPro)
            {
                m_TextMeshPro.ForceMeshUpdate();
                m_cachedMeshInfoVertexData = m_TextMeshPro.textInfo.CopyMeshInfoVertexData();
            }
        }

        void LateUpdate()
        {
            if (!isHoveringObject || m_TextMeshPro.textInfo.characterCount == 0)
                return;

            int charIndex = TMP_TextUtilities.FindIntersectingCharacter(
                m_TextMeshPro, Input.mousePosition, m_Camera, true);

            if (charIndex == -1 || charIndex != m_lastIndex)
            {
                RestoreCachedVertexAttributes(m_lastIndex);
                m_lastIndex = -1;
            }

            if (charIndex != -1 && charIndex != m_lastIndex &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                m_lastIndex = charIndex;

                var charInfo = m_TextMeshPro.textInfo.characterInfo[charIndex];
                int materialIndex = charInfo.materialReferenceIndex;
                int vertexIndex = charInfo.vertexIndex;

                Vector3[] vertices = m_TextMeshPro.textInfo.meshInfo[materialIndex].vertices;

                Vector2 mid = (vertices[vertexIndex] + vertices[vertexIndex + 2]) / 2f;
                Vector3 offset = mid;

                for (int i = 0; i < 4; i++)
                    vertices[vertexIndex + i] -= offset;

                m_matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 1.5f);

                for (int i = 0; i < 4; i++)
                    vertices[vertexIndex + i] = m_matrix.MultiplyPoint3x4(vertices[vertexIndex + i]);

                for (int i = 0; i < 4; i++)
                    vertices[vertexIndex + i] += offset;

                Color32[] colors = m_TextMeshPro.textInfo.meshInfo[materialIndex].colors32;
                Color32 c = new Color32(255, 255, 192, 255);

                for (int i = 0; i < 4; i++)
                    colors[vertexIndex + i] = c;

                m_TextMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHoveringObject = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHoveringObject = false;
            RestoreCachedVertexAttributes(m_lastIndex);
            m_lastIndex = -1;
        }

        public void OnPointerClick(PointerEventData eventData) { }
        public void OnPointerUp(PointerEventData eventData) { }

        void RestoreCachedVertexAttributes(int index)
        {
            if (index < 0 ||
                index >= m_TextMeshPro.textInfo.characterCount ||
                m_cachedMeshInfoVertexData == null)
                return;

            var charInfo = m_TextMeshPro.textInfo.characterInfo[index];
            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;

            Vector3[] srcVerts = m_cachedMeshInfoVertexData[materialIndex].vertices;
            Vector3[] dstVerts = m_TextMeshPro.textInfo.meshInfo[materialIndex].vertices;

            Color32[] srcCols = m_cachedMeshInfoVertexData[materialIndex].colors32;
            Color32[] dstCols = m_TextMeshPro.textInfo.meshInfo[materialIndex].colors32;

            for (int i = 0; i < 4; i++)
            {
                dstVerts[vertexIndex + i] = srcVerts[vertexIndex + i];
                dstCols[vertexIndex + i] = srcCols[vertexIndex + i];
            }

            m_TextMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
        }
    }
}
