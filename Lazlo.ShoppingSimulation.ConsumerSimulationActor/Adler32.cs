using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Lazlo.ShoppingSimulation.ConsumerSimulationActor
{
    public class Adler32 : HashAlgorithm
    {
        uint _A = 1;
        uint _B = 0;

        public override int HashSize { get { return 32; } }

        public override void Initialize()
        {
            _A = 1;
            _B = 0;
        }

        // https://software.intel.com/en-us/articles/fast-computation-of-adler32-checksums
        // For the pseudo algorithm
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            for (int i = 0; i < cbSize; i += 5552)
            {
                for (int j = 0; j < 5552 && i + j < cbSize; j++)
                {
                    _A += array[ibStart + i + j];
                    _B += _A;
                }

                _A = (_A % 65521);
                _B = (_B % 65521);
            }
        }

        protected override byte[] HashFinal()
        {
            return BitConverter.GetBytes(_B << 16 | _A);
        }
    }
}
