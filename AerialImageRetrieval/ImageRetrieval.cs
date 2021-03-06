﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ImageMagick;
using Microsoft.MapPoint;

namespace AerialImageRetrieval
{
    public class ImageRetrieval
    {
        /// <summary>
        /// The maximum zoom level in Bing Maps.
        /// </summary>
        public const int MaxLevel = 23;

        /// <summary>
        /// Determines if the image contains labels, such as street names and points of interest.
        /// </summary>
        public bool Labeled { get; set; } = true;

        /// <summary>
        /// Determines if downloaded tiles are cached locally.
        /// </summary>
        public bool CacheTiles { get; set; } = true;

        /// <summary>
        /// The output format.
        /// </summary>
        public MagickFormat ImageFormat { get; set; } = MagickFormat.Png24;

        private string culture = "en-us";
        /// <summary>
        /// The culture code for localized map labels.
        /// </summary>
        public string Culture
        {
            get => Culture;
            set
            {
                foreach (char c in value)
                    if (!(char.IsLetter(c) ||  c == '-'))
                        throw new ArgumentException("Value is not a valid culture code");

                culture = value;
            }
        }

        private const string AerialLabeledUrl =
            "https://t.ssl.ak.tiles.virtualearth.net/tiles/h{0}.jpeg?g=517&mkt={1}";

        private const string AerialUnlabeledUrl =
            "https://t.ssl.ak.tiles.virtualearth.net/tiles/a{0}.jpeg?g=517&mkt={1}";

        private string BaseUrl => Labeled ? AerialLabeledUrl : AerialUnlabeledUrl;

        private const int TileSize = 256; // side length of a tile in Bing's tile system

        private string appDataFolder;
        private string CacheFolder =>
            Path.Combine(appDataFolder, Labeled ? "cache/labeled" : "cache/unlabeled");

        private byte[] nullImg;

        public ImageRetrieval() 
        {
            appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AerialImageRetrieval");
        }

        /// <summary>
        /// Retrieves aerial imagery with the highest possible zoom level.
        /// </summary>
        /// <param name="lat1">Latitude of the top left corner.</param>
        /// <param name="lon1">Longitude of the top left corner.</param>
        /// <param name="lat2">Latitude of the bottom right corner.</param>
        /// <param name="lon2">Longitude of the bottom right corner.</param>
        /// <param name="outputPath">Path of the output file.</param>
        /// <param name="maxLevel">Caps the highest zoom level the method will request
        /// at the given value.</param>
        /// <returns>Returns whether the method succeeded.</returns>
        public bool RetrieveMaxResolution(double lat1, double lon1, double lat2, double lon2, 
            string outputPath, int maxLevel = MaxLevel)
        {
            var image = RetrieveMaxResolution(lat1, lon1, lat2, lon2, maxLevel);
            if (image is null) 
                return false;
            SaveImage(outputPath, image);
            return true;
        }

        /// <summary>
        /// Retrieves aerial imagery with the highest possible zoom level.
        /// </summary>
        /// <param name="lat1">Latitude of the top left corner.</param>
        /// <param name="lon1">Longitude of the top left corner.</param>
        /// <param name="lat2">Latitude of the bottom right corner.</param>
        /// <param name="lon2">Longitude of the bottom right corner.</param>
        /// <param name="maxLevel">Caps the highest zoom level the method will request
        /// at the given value.</param>
        /// <returns>Returns the image if the method succeeded, or null if it didn't.</returns>
        public IMagickImage RetrieveMaxResolution(double lat1, double lon1, double lat2, double lon2,
            int maxLevel = MaxLevel)
        {
            /* The main aerial retrieval method

               It will firstly determine the appropriate level used to retrieve the image.
               All the tile image within the given bounding box at that level should all exist.

               Then for the given level, we can download each aerial tile image, and stitch them together.

               Lastly, we have to crop the image based on the given bounding box
            */

            if (maxLevel < 0)
                throw new ArgumentOutOfRangeException("maxLevel");

            Directory.CreateDirectory(CacheFolder);

            for (int level = maxLevel; level >= 0; level--)
            {
                TileSystem.LatLongToPixelXY(lat1, lon1, level, out var pixelX1, out var pixelY1);
                TileSystem.LatLongToPixelXY(lat2, lon2, level, out var pixelX2, out var pixelY2);

                (pixelX1, pixelX2) = (Math.Min(pixelX1, pixelX2), Math.Max(pixelX1, pixelX2));
                (pixelY1, pixelY2) = (Math.Min(pixelY1, pixelY2), Math.Max(pixelY1, pixelY2));

                // Bounding box's two coordinates coincide at the same pixel, which is invalid for an aerial image.
                // Raise error and directly return without retriving any valid image.
                if (Math.Abs(pixelX1 - pixelX2) <= 1 || Math.Abs(pixelY1 - pixelY2) <= 1)
                {
                    throw new ArgumentException("Cannot find a valid aerial imagery for the given bounding box!");
                }

                TileSystem.PixelXYToTileXY(pixelX1, pixelY1, out var tileX1, out var tileY1);
                TileSystem.PixelXYToTileXY(pixelX2, pixelY2, out var tileX2, out var tileY2);

                // Download tiles and stitch them together
                using var collection = new MagickImageCollection();

                var retrieveSuccess = DownloadTiles(tileX1, tileY1, tileX2, tileY2, level, collection);
                if (!retrieveSuccess)
                    continue;

                var result = collection.Montage(new MontageSettings()
                {
                    Geometry = new MagickGeometry(TileSize, TileSize),
                    TileGeometry = new MagickGeometry(tileX2 - tileX1 + 1, tileY2 - tileY1 + 1),
                });

                // Crop the image based on the given bounding box
                TileSystem.TileXYToPixelXY(tileX1, tileY1, out var leftup_cornerX, out var leftup_cornerY);
                int x = (int)(pixelX1 - leftup_cornerX);
                int y = (int)(pixelY1 - leftup_cornerY);
                int width = (int)(pixelX2 - leftup_cornerX) - x;
                int height = (int)(pixelY2 - leftup_cornerY) - y;
                result.Crop(new MagickGeometry(x, y, width, height));
                result.RePage();
                return result;
            }
            return null;
        }

        /// <summary>
        /// Overrides the default temp directory of ImageMagick.
        /// </summary>
        /// <param name="dir"></param>
        public void SetMagickTempDirectory(string dir)
        {
            Directory.CreateDirectory(dir);
            MagickNET.SetTempDirectory(dir);
        }

        /// <summary>
        /// Downloads tiles to the specified MagickImageCollection.
        /// </summary>
        /// <param name="tileXStart"></param>
        /// <param name="tileYStart"></param>
        /// <param name="tileXEnd"></param>
        /// <param name="tileYEnd"></param>
        /// <param name="level"></param>
        /// <param name="collection"></param>
        /// <returns>Returns whether the download succeeded.</returns>
        private bool DownloadTiles(int tileXStart, int tileYStart, int tileXEnd, int tileYEnd,
            int level, MagickImageCollection collection)
        {
            for (int tileY = tileYStart; tileY < tileYEnd + 1; tileY++)
            {
                for (int tileX = tileXStart; tileX < tileXEnd + 1; tileX++)
                {
                    var quadKey = TileSystem.TileXYToQuadKey(tileX, tileY, level);
                    var image = DownloadImage(quadKey);
                    if (image is null)
                    {
                        Trace.WriteLine($"Cannot find tile image at level {level} for tile coordinate ({tileX}, {tileY})");
                        return false;
                    }
                    else
                    {
                        collection.Add(image);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Downloads a tile with the given QuadKey.
        /// </summary>
        /// <param name="quadKey"></param>
        /// <returns></returns>
        private MagickImage DownloadImage(string quadKey)
        {
            byte[] data = null;

            var cachePath = Path.Combine(CacheFolder, quadKey + ".jpg");
            if (CacheTiles && File.Exists(cachePath))
            {
                data = File.ReadAllBytes(cachePath);
            }
            else
            {
                using var wc = new WebClient();
                try
                {
                    data = wc.DownloadData(string.Format(BaseUrl, quadKey, culture));
                }
                catch (WebException wex)
                {
                    // just let data be null
                    Trace.WriteLine(wex.ToString());
                }

                if (CacheTiles)
                { 
                    Task.Run(() => 
                    { 
                        try 
                        { 
                            File.WriteAllBytesAsync(cachePath, data);
                        }
                        catch (IOException ioex)
                        {
                            // guess we're not caching this one then.
                            Trace.WriteLine(ioex.ToString());
                        }
                    });
                }
            }

            if (data is null || IsNullImage(data))
                return null;
            else
                return new MagickImage(data);
        }

        /// <summary>
        /// Checks if an image is the "unavailable" tile.
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        private bool IsNullImage(byte[] image)
        {
            if (nullImg is null)
            {
                // an invalid quadkey which will download a null jpeg
                using (var wc = new WebClient())
                {
                    nullImg = wc.DownloadData(string.Format(BaseUrl, "11111111111111111111", culture));
                }
            }
            return image.SequenceEqual(nullImg);
        }

        /// <summary>
        /// Saves an image as PNG.
        /// </summary>
        /// <param name="outputPath"></param>
        /// <param name="result"></param>
        private void SaveImage(string outputPath, IMagickImage result)
        {
            using var fs = new FileStream(outputPath, FileMode.Create);
            result.Write(fs, ImageFormat);
        }

    }
}
