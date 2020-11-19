using System;
using System.IO;

using Atomex.Common;

namespace atomex_frontend.Common
{
    public class WebFileSystem : IFileSystem
    {
        public string PathToDocuments => BaseDirectory;
        public string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;
        public string AssetsDirectory => BaseDirectory;

        public Stream GetResourceStream(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.Read);

        public string ToFullPath(string path) => "/";
    }
}