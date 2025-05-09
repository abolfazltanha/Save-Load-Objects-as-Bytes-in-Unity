using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class FinalWrapper
{
    public string baseJson;
    public string animatorJson;
}



[System.Serializable]
public class MeshData
{
    public Vector3[] vertices;
    public int[] triangles;
    public Vector3[] normals;
    public Vector2[] uv;
}

[System.Serializable]
public class SerializableMaterial
{
    public string materialName;
    public string shaderName;
    public Color color;
    public float metallic;
    public float smoothness;
    public string textureBase64;
    public string renderMode;
}

[System.Serializable]
public class SerializableKeyframe
{
    public float time;
    public float value;
    public float inTangent;
    public float outTangent;

    public SerializableKeyframe() { }

    public SerializableKeyframe(Keyframe key)
    {
        time = key.time;
        value = key.value;
        inTangent = key.inTangent;
        outTangent = key.outTangent;
    }

    public Keyframe ToKeyframe()
    {
        return new Keyframe(time, value, inTangent, outTangent);
    }
}

[System.Serializable]
public class SerializableCurve
{
    public string path;
    public string propertyName;
    public List<SerializableKeyframe> keyframes = new();

    public AnimationCurve ToAnimationCurve()
    {
        List<Keyframe> keys = new();
        foreach (var k in keyframes)
        {
            keys.Add(k.ToKeyframe());
        }
        return new AnimationCurve(keys.ToArray());
    }
}

[System.Serializable]
public class SerializableClipData
{
    public string name;
    public float length;
    public List<SerializableCurve> curves = new();

    public AnimationClip ToAnimationClip()
    {
        AnimationClip clip = new AnimationClip();
        clip.name = name;
        foreach (var sc in curves)
        {
            clip.SetCurve(sc.path, typeof(Transform), sc.propertyName, sc.ToAnimationCurve());
        }
        return clip;
    }
}

[System.Serializable]
public class SerializableAnimationClip
{
    public string name;
    public SerializableClipData serializedClip;
}

[System.Serializable]
public class SerializableAnimator
{
    public List<SerializableAnimationClip> clips = new();
}

[System.Serializable]
public class SerializableTransform
{
    public string name;
    public bool isActive;
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public MeshData meshData;
    public List<SerializableMaterial> materials = new();
    public List<SerializableTransform> children = new();
    public SerializableAnimator animator;
}

public class Save_Load_Mesh : MonoBehaviour
{
    public static class ObjectSerializer
    {
        public static SerializableClipData ExtractClipData(AnimationClip clip)
        {
            var data = new SerializableClipData
            {
                name = clip.name,
                length = clip.length
            };

#if UNITY_EDITOR
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var b in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                var serialCurve = new SerializableCurve
                {
                    path = b.path,
                    propertyName = b.propertyName
                };
                foreach (var k in curve.keys)
                {
                    serialCurve.keyframes.Add(new SerializableKeyframe(k));
                }
                data.curves.Add(serialCurve);
            }
#endif
            return data;
        }

        public static SerializableTransform ExtractHierarchy(Transform root)
        {
            var serial = new SerializableTransform
            {
                name = root.name,
                isActive = root.gameObject.activeSelf,
                localPosition = root.localPosition,
                localRotation = root.localRotation,
                localScale = root.localScale,
                meshData = null
            };

            var mf = root.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                serial.meshData = new MeshData
                {
                    vertices = mf.sharedMesh.vertices,
                    triangles = mf.sharedMesh.triangles,
                    normals = mf.sharedMesh.normals,
                    uv = mf.sharedMesh.uv
                };
            }

            var renderer = root.GetComponent<MeshRenderer>();
            if (renderer && renderer.sharedMaterials != null)
            {
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;
                    var sm = new SerializableMaterial
                    {
                        materialName = mat.name,
                        shaderName = mat.shader.name,
                        color = mat.HasProperty("_Color") ? mat.color : Color.white,
                        metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f,
                        smoothness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0f
                    };
                    if (mat.shader.name == "Standard" && mat.HasProperty("_Mode"))
                    {
                        int mode = (int)mat.GetFloat("_Mode");
                        switch (mode)
                        {
                            case 0: sm.renderMode = "Opaque"; break;
                            case 1: sm.renderMode = "Cutout"; break;
                            case 2: sm.renderMode = "Fade"; break;
                            case 3: sm.renderMode = "Transparent"; break;
                        }
                    }

                    if (mat.mainTexture is Texture2D tex)
                    {
                        byte[] texBytes = tex.EncodeToPNG();
                        sm.textureBase64 = System.Convert.ToBase64String(texBytes);
                    }

                    serial.materials.Add(sm);
                }
            }

            var sa = new SerializableAnimator();
            var animation = root.GetComponent<Animation>();
            if (animation != null)
            {
                foreach (AnimationState state in animation)
                {
                    var clip = state.clip;
                    var serialized = ExtractClipData(clip);
                    sa.clips.Add(new SerializableAnimationClip
                    {
                        name = clip.name,
                        serializedClip = serialized
                    });
                }
            }
            else
            {
                var animator = root.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    foreach (var clip in animator.runtimeAnimatorController.animationClips)
                    {
                        var serialized = ExtractClipData(clip);
                        sa.clips.Add(new SerializableAnimationClip
                        {
                            name = clip.name,
                            serializedClip = serialized
                        });
                    }
                }
            }

            if (sa.clips.Count > 0)
                serial.animator = sa;

            foreach (Transform child in root)
            {
                serial.children.Add(ExtractHierarchy(child));
            }

            return serial;
        }

        public static string SerializeObject(Transform root)
        {
            SerializableTransform baseData = ExtractHierarchy(root);

            SerializableAnimator animator = baseData.animator;
            baseData.animator = null;

            FinalWrapper wrap = new FinalWrapper
            {
                baseJson = JsonUtility.ToJson(baseData),
                animatorJson = JsonConvert.SerializeObject(animator, Formatting.None, new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    MaxDepth = 10,
                    Error = (sender, args) =>
                    {
                        Debug.LogWarning("Animation serialization error: " + args.ErrorContext.Error.Message);
                        args.ErrorContext.Handled = true;
                    }
                })
            };

            string json = JsonUtility.ToJson(wrap);
            return CompressAndEncode(json);
        }


        public static SerializableTransform DeserializeObject(string base64)
        {
            string json = DecodeAndDecompress(base64);
            FinalWrapper wrap = JsonUtility.FromJson<FinalWrapper>(json);

            SerializableTransform baseData = JsonUtility.FromJson<SerializableTransform>(wrap.baseJson);

            if (!string.IsNullOrEmpty(wrap.animatorJson))
            {
                baseData.animator = JsonConvert.DeserializeObject<SerializableAnimator>(wrap.animatorJson);
            }

            return baseData;
        }

    }

    public static string CompressAndEncode(string json)
    {
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var output = new System.IO.MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            gzip.Write(jsonBytes, 0, jsonBytes.Length);
        }
        return System.Convert.ToBase64String(output.ToArray());
    }

    public static string DecodeAndDecompress(string base64)
    {
        byte[] compressedData = System.Convert.FromBase64String(base64);
        using var input = new System.IO.MemoryStream(compressedData);
        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new System.IO.StreamReader(gzip);
        return reader.ReadToEnd();
    }


    public void SaveFullObject(Transform obj)
    {
        string encoded = ObjectSerializer.SerializeObject(obj);
        PlayerPrefs.SetString("FullObject", encoded);
        PlayerPrefs.Save();
        Debug.Log("Object hierarchy saved.");
    }

    public GameObject RebuildHierarchy(SerializableTransform data)
    {
        GameObject go = new GameObject(data.name);
        go.SetActive(data.isActive);

        go.transform.localPosition = data.localPosition;
        go.transform.localRotation = data.localRotation;
        go.transform.localScale = data.localScale;

        if (data.meshData != null)
        {
            var mf = go.AddComponent<MeshFilter>();
            Mesh mesh = new Mesh
            {
                vertices = data.meshData.vertices,
                triangles = data.meshData.triangles,
                normals = data.meshData.normals,
                uv = data.meshData.uv
            };
            mesh.RecalculateBounds();
            mf.mesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            List<Material> mats = new();
            foreach (var sm in data.materials)
            {
                Material mat = new Material(Shader.Find(sm.shaderName));
                if (mat.HasProperty("_Color")) mat.color = sm.color;
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", sm.metallic);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", sm.smoothness);

                if (!string.IsNullOrEmpty(sm.textureBase64))
                {
                    byte[] texBytes = System.Convert.FromBase64String(sm.textureBase64);
                    Texture2D tex = new Texture2D(2, 2);
                    tex.LoadImage(texBytes);
                    mat.mainTexture = tex;
                }
                mat.name = sm.materialName;
                mats.Add(mat);
            }
            mr.materials = mats.ToArray();
        }

        foreach (var childData in data.children)
        {
            GameObject childGO = RebuildHierarchy(childData);
            childGO.transform.SetParent(go.transform, false);
        }

        if (data.animator != null && data.animator.clips.Count > 0)
        {
            Animation anim = go.GetComponent<Animation>();
            if (anim == null)
                anim = go.AddComponent<Animation>();

            AnimationClip firstClip = null;

            foreach (var clipData in data.animator.clips)
            {
                if (clipData.serializedClip != null)
                {
                    AnimationClip clip = clipData.serializedClip.ToAnimationClip();
                    clip.legacy = true;

                    if (!anim.GetClip(clip.name))
                        anim.AddClip(clip, clip.name);

                    if (firstClip == null)
                        firstClip = clip;
                }
            }

            if (firstClip != null)
            {
                anim.clip = firstClip;
                anim.Play();
            }
        }

        return go;
    }

    public void LoadFullObject()
    {
        if (!PlayerPrefs.HasKey("FullObject")) return;

        string base64 = PlayerPrefs.GetString("FullObject");
        SerializableTransform data = ObjectSerializer.DeserializeObject(base64);

        GameObject result = RebuildHierarchy(data);
        result.transform.position = Vector3.zero;
        Debug.Log("Object hierarchy loaded.");
    }
}
