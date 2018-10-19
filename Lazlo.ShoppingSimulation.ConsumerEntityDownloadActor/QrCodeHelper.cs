using ImageMagick;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ZXing;
using ZXing.Common;
using ZXing.OneD;
using ZXing.QrCode;

namespace Lazlo.ShoppingSimulation.ConsumerEntityDownloadActor
{
    public class QrCodeHelper
    {
        #region Parsing
        public static string ParseImage(byte[] imageBytes)
        {
            return ParseImageViaMagick(imageBytes);
        }

        public static string ParseImageViaSkia(byte[] imageBytes)
        {
            ZXing.SkiaSharp.BarcodeReader reader = new ZXing.SkiaSharp.BarcodeReader();

            reader.Options.TryHarder = true;

            using (SKBitmap bitmap = SKBitmap.Decode(imageBytes))
            {
                return reader.Decode(bitmap)?.Text;
            }
        }

        public static string ParseImageViaMagick(byte[] imageBytes)
        {
            try
            {
                ZXing.Magick.BarcodeReader reader = new ZXing.Magick.BarcodeReader();

                reader.Options.TryHarder = true;

                using (MagickImage image = new MagickImage(imageBytes))
                {
                    return reader.Decode(image)?.Text;
                }
            }

            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                throw;
            }
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Encodes provided info param into a qrcode image
        /// </summary>
        /// <param name="info"></param>
        /// <returns>Base64 of image</returns>
        public static byte[] GenerateQrCodeJpg(string info, int margin, int width, int height)
        {
            return GenerateQrCodeViaSkia(info, 0, 200, 200);
        }

        public static byte[] GenerateQrCodePng(string info, int margin, int width, int height)
        {
            IDictionary<EncodeHintType, object> hints = new Dictionary<EncodeHintType, object>();

            hints.Add(EncodeHintType.MARGIN, margin);

            QRCodeWriter qr = new QRCodeWriter();

            BitMatrix matrix = qr.encode(info, ZXing.BarcodeFormat.QR_CODE, width, height, hints);

            return matrix.ToPng();
        }

        public static byte[] GenerateQrCodeViaSkia(string info, int margin, int width, int height)
        {
            IDictionary<EncodeHintType, object> hints = new Dictionary<EncodeHintType, object>();

            hints.Add(EncodeHintType.MARGIN, margin);

            QRCodeWriter qr = new QRCodeWriter();

            BitMatrix matrix = qr.encode(info, ZXing.BarcodeFormat.QR_CODE, width, height, hints);

            using (SKBitmap bitmap = SKBitmap.Decode(matrix.ToPng()))
            using (SKImage image = SKImage.FromBitmap(bitmap))
            {
                return image.Encode(SKEncodedImageFormat.Jpeg, 100).ToArray();
            }
        }

        public static byte[] GenerateQrCodeViaMagick(string info, int margin, int width, int height)
        {
            IDictionary<EncodeHintType, object> hints = new Dictionary<EncodeHintType, object>();

            hints.Add(EncodeHintType.MARGIN, margin);

            QRCodeWriter qr = new QRCodeWriter();

            BitMatrix matrix = qr.encode(info, ZXing.BarcodeFormat.QR_CODE, width, height, hints);

            using (MagickImage magickImage = new MagickImage(matrix.ToPng()))
            {
                return magickImage.ToByteArray(MagickFormat.Jpeg);
            }
        }

        public static byte[] GenerateEanThirteen(string eanThirteenCode, int width, int height)
        {
            EAN13Writer writer = new EAN13Writer();

            BitMatrix matrix = writer.encode(eanThirteenCode, BarcodeFormat.EAN_13, width, height);

            return matrix.ToPng();
        }

        // http://www.cev.ie/scales-and-scanners-price-embedded-barcodes/
        public static string CreateValidEanThirteenCode(string productCode, decimal price)
        {
            Regex regex = new Regex(@"^(\d{5})$");

            if (!regex.IsMatch(productCode))
            {
                throw new ArgumentException("Invalid product code");
            }

            if (price < 0M || price > 999.99M)
            {
                throw new ArgumentException("Price must be between 0 and 999.99");
            }

            string priceString = price.ToString("F").Replace(".", "");

            if (price < 10M)
            {
                priceString = priceString.PadLeft(4, '0');
            }

            StringBuilder stringBuilder = new StringBuilder();

            string codeWithoutChecksum;

            switch (priceString.Length)
            {
                case 4:
                    {
                        int priceCheck = Ean13Checksum(priceString);

                        codeWithoutChecksum = $"02{productCode}{priceCheck}{priceString}";

                        break;
                    }


                case 5:
                    {
                        codeWithoutChecksum = $"02{productCode}{priceString}";

                        break;
                    }


                default:
                    throw new ArgumentException("Unsupported price");

            }

            int codeChecksum = Ean13Checksum(codeWithoutChecksum);

            return $"{codeWithoutChecksum}{codeChecksum}";
        }

        private static int Ean13Checksum(string digits)
        {
            int sum = 0;

            for (int i = digits.Length; i >= 1; i--)
            {
                int digit = (int)Char.GetNumericValue(digits[i - 1]);

                if (i % 2 == 0)
                {
                    sum += digit * 3;
                }

                else
                {
                    sum += digit * 1;
                }
            }

            return (10 - (sum % 10)) % 10;
        }
        #endregion
    }
}
