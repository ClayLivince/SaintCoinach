using Godbert.Controls;
using Godbert.Repositories;
using SaintCoinach.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Godbert.Models {
    public class ScannedIcon {

        const string UiImagePathFormat = "ui/icon/{0:D3}000{1}/{2:D6}{3}.tex";
        public IconRepository IconRepository { get; set; }
        
        public int ID { get; set; }
        public string DisplayId => $"{ID:D6}";
        public bool IsLanguageTyped { get; set; }
        public bool HasHQVariant { get; set; }
        public ImageFile Icon { get; set; }

        private Dictionary<(string, string), WeakReference<ImageSource>> _bitmapByVariant = new();

        #region Bitmaps
        public ImageSource Bitmap {
            get => GetImageSourceByVariant(IconRepository.Parent.IsIconHQ ? "/hq" : "", IconRepository.Parent.IsIconHr1 ? "_hr1" : "");
        }
        #endregion
        public int Width { get => Icon.Width; }
        public int Height { get => Icon.Height; }

        public ScannedIcon(IconRepository repo, int id, ImageFile icon, bool hasHQVariant=false, bool isLanguageTyped=false) {
            IconRepository = repo;
            ID = id;
            Icon = icon;
            HasHQVariant = hasHQVariant;
            IsLanguageTyped = isLanguageTyped;
        }

        private ImageFile GetImageOfType(string subset, string res) {
            if (Icon == null)
                return null;
            
            if (!HasHQVariant && subset == "/hq")
                subset = "";

            if (IsLanguageTyped && string.IsNullOrEmpty(subset))
                subset = IconRepository.GetCurrentLanguageVariant();

            string path = string.Format(UiImagePathFormat, ID / 1000, subset, ID, res);
            if (Icon.Pack.TryGetFile(path, out var file)) {
                ImageFile imgFile = file as ImageFile;
                return imgFile;
            }

            // It goes here, means requested icon do not exist, 
            // so we do some fallback here.
            if (subset != "" & !IsLanguageTyped) {
                return GetImageOfType("", res);
            }

            // Well, although no reason for fallback subset first, anyway there needs a sequence.
            if (res != "") {
                return GetImageOfType(subset, "");
            }

            return null;
        }

        private ImageSource GetImageSourceByVariant(string subset, string res) {
            if (!HasHQVariant && subset == "/hq")
                subset = "";

            if (IsLanguageTyped && string.IsNullOrEmpty(subset))
                subset = IconRepository.GetCurrentLanguageVariant();

            if (_bitmapByVariant.TryGetValue((subset, res), out var bitmapRef)) {
                if (bitmapRef.TryGetTarget(out var bitmap)) {
                    return bitmap;
                }
            }

            ImageFile imgFile = GetImageOfType(subset, res);
            if (imgFile != null) {
                var bitmap = CreateSource(imgFile);

                _bitmapByVariant[(subset, res)] = new WeakReference<ImageSource>(bitmap);
                return bitmap;
            }

            // Unsubsetted image don't exist either, return the default image.
            return DefaultImage;
        }

        public void Save(string path, string subset, string res) {
            ImageFile imgFile = GetImageOfType(subset, res);
            var img = imgFile.GetImage();
            img.Save(path);
        }

        private static ImageSource DefaultImage = BitmapSource.Create(10, 10, 96, 96,
            PixelFormats.Bgr32, null,
            new byte[(10 * PixelFormats.Bgr32.BitsPerPixel + 7) / 8 * 10],
            (10 * PixelFormats.Bgr32.BitsPerPixel + 7) / 8);

        private static ImageSource CreateSource(ImageFile file) {
            var argb = ImageConverter.GetA8R8G8B8(file);
            return BitmapSource.Create(file.Width, file.Height,
                96, 96,
                PixelFormats.Bgra32, null,
                argb, file.Width * 4);
        }
    }
}
