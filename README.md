**AerialImageRetrieval** ia a small module for downloading aerial/satellite images from Bing Maps.

The code is based on [llgeek/Satellite-Aerial-Image-Retrieval](https://github.com/llgeek/Satellite-Aerial-Image-Retrieval).

## How to use it

    var ir = new ImageRetrieval();
    ir.RetrieveMaxResolution(51.611650, -0.340893, 51.371810, 0.112293, "./output.png", 13);

Simply pass the top left and bottom right corner of your bounding box, the path to the output file, and the zoom level.
If tiles of the given zoom level don't exist, a lower level will be used.

Additional properties of the class are:

* **CacheTiles**: You can cache tiles locally if you want to create multiple images from the same tiles. Enabled by default.
* **Culture**: Sets the [culture code](https://docs.microsoft.com/en-us/bingmaps/rest-services/common-parameters-and-types/supported-culture-codes) of map labels.
* **ImageFormat**: Sets the output format.
* **Labeled**: Sets if map labels such as city names, street names etc. are included in the image. Enabled by default.  
  (This may or may not be the deprecated tileset. I'm not sure.)

## Dependencies
* [Magick.NET](https://github.com/dlemstra/Magick.NET/)
