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

    private GameObject currentModel;
    private bool loading;

    void OnEnable()  => qrReader.OnQRDetected += HandleQR;
    void OnDisable() => qrReader.OnQRDetected -= HandleQR;

    private async void HandleQR(string url)
    {
        // Ignore new QR codes while a model is still being downloaded
        if (loading) return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            Log("The QR code does not contain a valid URL");
            return;
        }

        loading = true;
        try
        {
            Log("Downloading model...");
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

    private void Log(string msg)
    {
        Debug.Log(msg);
        if (debugText != null) debugText.text = msg;
    }
}