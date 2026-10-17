using System.Collections.Generic;
using UnityEngine;

/// <summary>Small things the effects need and nothing in the project supplies: a soft dot, a flat disc, a plain
/// unlit material. Built in code so there is nothing to import.</summary>
public static class ProceduralArt
{
    static readonly Dictionary<Color, Material> shared = new Dictionary<Color, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { shared.Clear(); }

    /// <summary>A round, soft-edged white dot with a smooth fall-off. The default square sprite reads as blocks.</summary>
    public static Texture2D SoftDot(int size = 32)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "SoftDot",
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float edge = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, edge * edge * (3f - 2f * edge)));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>A round marking for the ground: a faint fill with a bright rim, to be tinted. Used for hazard zones.</summary>
    public static Texture2D ZoneRim(int size = 128)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ZoneRim",
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);

                float fill = Mathf.Lerp(0.55f, 0.95f, r);                              // fainter in the middle
                float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.94f) / 0.05f);          // a bright ring at the edge
                float alpha = Mathf.Max(fill * 0.6f, rim) * Mathf.Clamp01((1f - r) / 0.015f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>A flat disc in the XZ plane, facing up, centred on the origin.</summary>
    public static Mesh Disc(float radius, int segments = 48)
    {
        var vertices = new Vector3[segments + 1];
        var uv = new Vector2[segments + 1];
        var triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;
        uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            vertices[i + 1] = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            uv[i + 1] = new Vector2(0.5f + Mathf.Sin(a) * 0.5f, 0.5f + Mathf.Cos(a) * 0.5f);   // the texture spans the disc
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = (i + 1) % segments + 1;      // wound so the face points up
            triangles[i * 3 + 2] = i + 1;
        }

        var mesh = new Mesh { name = "Disc" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>A see-through unlit material of one colour (and, optionally, one texture), for markers and sparks.</summary>
    public static Material Unlit(Color colour, Texture texture = null)
    {
        var shader = Shader.Find("Sprites/Default");
        var material = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader")) { color = colour };
        if (texture != null) material.mainTexture = texture;
        return material;
    }

    /// <summary>A plain lit material of one colour, for props.</summary>
    public static Material Lit(Color colour)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = new Material(shader != null ? shader : Shader.Find("Standard")) { color = colour };
        material.SetFloat("_Smoothness", 0.15f);
        return material;
    }

    /// <summary>One lit material per colour, shared by every prop of that colour (a debris pile is dozens of pieces).</summary>
    public static Material SharedLit(Color colour)
    {
        if (shared.TryGetValue(colour, out var material) && material != null) return material;
        material = Lit(colour);
        material.hideFlags = HideFlags.HideAndDontSave;
        shared[colour] = material;
        return material;
    }
}
