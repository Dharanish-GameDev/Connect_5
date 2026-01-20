using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

namespace TMPro.Examples
{
    public class VertexZoom : MonoBehaviour
    {
        [Range(1f, 2f)]
        public float MinScale = 1.0f;

        [Range(1f, 2f)]
        public float MaxScale = 1.5f;

        public float UpdateDelay = 0.1f;

        private TMP_Text m_TextComponent;
        private bool hasTextChanged;

        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
            if (m_TextComponent == null)
            {
                enabled = false;
                return;
            }
        }

        void OnEnable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
            StartCoroutine(AnimateVertexZoom());
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            StopAllCoroutines();
        }

        void OnTextChanged(Object obj)
        {
            if (obj == m_TextComponent)
                hasTextChanged = true;
        }

        IEnumerator AnimateVertexZoom()
        {
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;
            TMP_MeshInfo[] cachedMeshInfo = textInfo.CopyMeshInfoVertexData();

            hasTextChanged = true;

            while (true)
            {
                if (hasTextChanged)
                {
                    m_TextComponent.ForceMeshUpdate();
                    textInfo = m_TextComponent.textInfo;
                    cachedMeshInfo = textInfo.CopyMeshInfoVertexData();
                    hasTextChanged = false;
                }

                int characterCount = textInfo.characterCount;
                if (characterCount == 0)
                {
                    yield return null;
                    continue;
                }

                for (int i = 0; i < characterCount; i++)
                {
                    TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
                    if (!charInfo.isVisible)
                        continue;

                    int materialIndex = charInfo.materialReferenceIndex;
                    int vertexIndex = charInfo.vertexIndex;

                    Vector3[] srcVertices = cachedMeshInfo[materialIndex].vertices;
                    Vector3[] dstVertices = textInfo.meshInfo[materialIndex].vertices;

                    Vector2 charMid = (srcVertices[vertexIndex] + srcVertices[vertexIndex + 2]) / 2f;
                    Vector3 offset = charMid;

                    for (int j = 0; j < 4; j++)
                        dstVertices[vertexIndex + j] = srcVertices[vertexIndex + j] - offset;

                    float scale = Random.Range(MinScale, MaxScale);
                    Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);

                    for (int j = 0; j < 4; j++)
                        dstVertices[vertexIndex + j] = matrix.MultiplyPoint3x4(dstVertices[vertexIndex + j]) + offset;
                }

                for (int i = 0; i < textInfo.meshInfo.Length; i++)
                {
                    textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
                    m_TextComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
                }

                yield return new WaitForSeconds(UpdateDelay);
            }
        }
    }
}
