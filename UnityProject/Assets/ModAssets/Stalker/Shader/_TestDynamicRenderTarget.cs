using System.Collections;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;

public class _TestDynamicRenderTarget : MonoBehaviour
{
    [SerializeField]
    public Material FullscreenMaterial = null;

    private RenderTexture currentRenderTexture = null;

    // Start is called before the first frame update
    void Start()
    {
        CreateRenderTexture();

        Shader shader = FullscreenMaterial.shader;
        for (int i = 0; i < shader.GetPropertyCount(); ++i) {
            string name = shader.GetPropertyName(i);
            Debug.Log("Shader property " + i + ": " + name);
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!currentRenderTexture.IsCreated() || (currentRenderTexture.width != Camera.main.pixelWidth || currentRenderTexture.height != Camera.main.pixelHeight))
        {
            CreateRenderTexture();
        }
    }

    void CreateRenderTexture()
    {
        Debug.Log("Creating Render Texture");

        var width = Camera.main.pixelWidth;
        var height = Camera.main.pixelHeight;
        currentRenderTexture = new RenderTexture(width, height, 0);

        // Update references
        FullscreenMaterial.SetTexture("_StalkerRenderTexture", currentRenderTexture);
        GameObject.Find("StalkerCamera").GetComponent<Camera>().targetTexture = currentRenderTexture;
    }
}
