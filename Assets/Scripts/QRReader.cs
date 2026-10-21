using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ZXing;

public class QRReader : MonoBehaviour
{
    [SerializeField] private ARCameraManager cameraManager;
    [SerializeField] private TMP_Text outputText;

    private IBarcodeReader reader;
    private Texture2D cameraTexture;
    private float scanInterval = 0.5f; 
    private float timer;
    private int framesProcessed = 0;
    private string lastResult = "";

    void Start()
    {
        reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE }
            }
        };
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < scanInterval) return;
        timer = 0f;

        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            if (outputText != null) outputText.text = "No image in the camera";
            return;
        }

        using (image)
        {
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(image.width / 2, image.height / 2),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.None
            };

            int size = image.GetConvertedDataSize(conversionParams);
            var buffer = new Unity.Collections.NativeArray<byte>(size, Unity.Collections.Allocator.Temp);

            image.Convert(conversionParams, buffer);

            if (cameraTexture == null ||
                cameraTexture.width != conversionParams.outputDimensions.x ||
                cameraTexture.height != conversionParams.outputDimensions.y)
            {
                cameraTexture = new Texture2D(
                    conversionParams.outputDimensions.x,
                    conversionParams.outputDimensions.y,
                    TextureFormat.RGBA32, false);
            }

            cameraTexture.LoadRawTextureData(buffer);
            cameraTexture.Apply();
            buffer.Dispose();

            framesProcessed++;

            var result = reader.Decode(
                cameraTexture.GetPixels32(),
                cameraTexture.width,
                cameraTexture.height);

            if (result != null)
            {
                if (result.Text != lastResult)
                {
                    lastResult = result.Text;
                    Debug.Log("QR read: " + result.Text);
                    Handheld.Vibrate(); 
                }

                if (outputText != null)
                    outputText.text = "READ: " + result.Text;
            }
        }
    }
}