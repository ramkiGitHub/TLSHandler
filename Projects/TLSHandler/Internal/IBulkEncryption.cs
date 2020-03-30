﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TLSHandler.Internal
{
    interface IBulkEncryption : IDisposable
    {
        int KeySize { get; }

    }
}
