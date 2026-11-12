using System;
using GLTFast;
using TMPro;
using UnityEngine;

public class QRModelLoader : MonoBehaviour
{
    [SerializeField] private QRReader qrReader;
    [SerializeField] private Camera arCamera;
    [SerializeField] private TMP_Text debugText;
    [SerializeField] private float distance = 1.5f;

    [Header("Only needed if the QR contains an ID instead of a URL")]
    [SerializeField] private string idPrefix = "XRQR:";
    [Tooltip("Use {id} as a placeholder, e.g. https://example.com/models/{id}.glb")]
    [SerializeField] private string urlTemplate = "";

    private GameObject currentModel;
    private bool loading;

    void OnEnable()  => qrReader.OnQRDetected += HandleQR;
    void OnDisable() => qrReader.OnQRDetected -= HandleQR;

    private async void HandleQR(string payload)
    {
        // Ignore new QR codes while a model is still being downloaded
        if (loading) return;

        string url = ResolveUrl(payload);
        if (url == null)
        {
            Log("Cannot resolve QR payload to a URL: " + payload);
            return;
        }

        loading = true;
        try
        {
            Log("Downloading model from: " + url);
            var gltf = new GltfImport();
            bool ok = await gltf.Load(url);
            if (!ok)
            {
                Log("Failed to download or load the model");
                return;
            }

            // Replace the previous model, if any
            if (currentModel != null) Destroy(currentModel);

            // Place the model in front of the camera
            currentModel = new GameObject("QRModel");
            Transform cam = arCamera.transform;
            currentModel.transform.SetPositionAndRotation(
                cam.position + cam.forward * distance,
                Quaternion.Euler(0, cam.eulerAngles.y, 0));

            ok = await gltf.InstantiateMainSceneAsync(currentModel.transform);
            Log(ok ? "Model loaded" : "Failed to instantiate the model");
        }
        catch (Exception e)
        {
            Log("Exception: " + e.Message);
            Debug.LogException(e);
        }
        finally
        {
            loading = false;
        }
    }

    // Returns a valid http(s) URL, or null if the payload can't be resolved
    private string ResolveUrl(string payload)
    {
        // Case 1: the QR already contains a URL
        if (Uri.TryCreate(payload, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "https" || uri.Scheme == "http"))
        {
            return payload;
        }

        // Case 2: the QR contains an ID, build the URL from the template
        if (!string.IsNullOrEmpty(urlTemplate) &&
            payload.StartsWith(idPrefix, StringComparison.Ordinal))
        {
            string id = payload.Substring(idPrefix.Length);
            return urlTemplate.Replace("{id}", id);
        }

        return null;
    }

    private void Log(string msg)
    {
        Debug.Log(msg);
        if (debugText != null) debugText.text = msg;
    }
}