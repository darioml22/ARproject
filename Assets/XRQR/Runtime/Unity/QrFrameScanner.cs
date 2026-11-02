using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ZXing;
using ZXing.Common;

namespace Xrqr.Unity
{
    /// <summary>
    /// Reads QR symbols from the AR camera: each scan takes the latest CPU image as 8-bit
    /// luminance and decodes it with ZXing.Net on a worker thread, one decode at a time, so the
    /// main thread only copies the frame.
    /// </summary>
    public sealed class QrFrameScanner : IDisposable
    {
        readonly ARCameraManager m_Camera;
        readonly BarcodeReaderGeneric m_Reader;
        byte[] m_Luminance = Array.Empty<byte>();
        Task<string> m_Decode;
        string m_LastText;
        float m_LastTime;
        float m_LastScan;

        /// <summary>The seconds between two scans.</summary>
        public float Interval = 0.25f;

        /// <summary>The seconds after which the same text counts as a new reading.</summary>
        public float RepeatDelay = 3f;

        public QrFrameScanner(ARCameraManager camera)
        {
            m_Camera = camera;
            m_Reader = new BarcodeReaderGeneric
            {
                AutoRotate = false,
                Options = new DecodingOptions
                {
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                    TryHarder = true,
                    CharacterSet = "UTF-8",
                },
            };
        }

        /// <summary>
        /// Advances the scanner by one frame. Returns the text of a symbol read since the last
        /// call, or <c>null</c>. The same text is reported again only after <see cref="RepeatDelay"/>.
        /// </summary>
        public string Tick()
        {
            if (m_Decode != null)
            {
                if (!m_Decode.IsCompleted)
                    return null;
                var text = m_Decode.Status == TaskStatus.RanToCompletion ? m_Decode.Result : null;
                m_Decode = null;
                if (text != null && (text != m_LastText || Time.unscaledTime - m_LastTime > RepeatDelay))
                {
                    m_LastText = text;
                    m_LastTime = Time.unscaledTime;
                    return text;
                }
                return null;
            }
            if (Time.unscaledTime - m_LastScan < Interval)
                return null;
            m_LastScan = Time.unscaledTime;
            if (m_Camera == null || !m_Camera.TryAcquireLatestCpuImage(out var image))
                return null;

            int width, height;
            using (image)
            {
                width = image.width;
                height = image.height;
                var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.R8, XRCpuImage.Transformation.None);
                var size = image.GetConvertedDataSize(conversion);
                if (m_Luminance.Length != size)
                    m_Luminance = new byte[size];
                using (var buffer = new NativeArray<byte>(size, Allocator.Temp))
                {
                    image.Convert(conversion, buffer);
                    buffer.CopyTo(m_Luminance);
                }
            }

            var luminance = m_Luminance;
            m_Decode = Task.Run(() =>
            {
                var source = new PlanarYUVLuminanceSource(luminance, width, height, 0, 0, width, height, false);
                return m_Reader.Decode(source)?.Text;
            });
            return null;
        }

        public void Dispose()
        {
            // A running decode owns the buffer until it ends; a new buffer is allocated next time.
            m_Luminance = Array.Empty<byte>();
        }
    }
}
