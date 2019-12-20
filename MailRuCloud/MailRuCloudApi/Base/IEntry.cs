﻿using System;
using System.IO;

namespace YaR.Clouds.Base
{
    public interface IEntry
    {
        bool IsFile { get; }
        FileSize Size { get; }
        string Name { get; }
        string FullPath { get; }
        DateTime CreationTimeUtc { get; }
        string PublicLink { get; }

        FileAttributes Attributes { get; }
    }
}