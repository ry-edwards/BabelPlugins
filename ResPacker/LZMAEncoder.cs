using System;

namespace LZMA
{
    internal abstract class EncodeBase
    {
        public const uint kNumRepDistances = 4;
        public const uint kNumStates = 12;

        // static byte []kLiteralNextStates  = {0, 0, 0, 0, 1, 2, 3, 4,  5,  6,   4, 5};
        // static byte []kMatchNextStates    = {7, 7, 7, 7, 7, 7, 7, 10, 10, 10, 10, 10};
        // static byte []kRepNextStates      = {8, 8, 8, 8, 8, 8, 8, 11, 11, 11, 11, 11};
        // static byte []kShortRepNextStates = {9, 9, 9, 9, 9, 9, 9, 11, 11, 11, 11, 11};

        public struct State
        {
            public uint Index;
            public void Init() { Index = 0; }
            public void UpdateChar()
            {
                if (Index < 4) Index = 0;
                else if (Index < 10) Index -= 3;
                else Index -= 6;
            }
            public void UpdateMatch() { Index = (uint)(Index < 7 ? 7 : 10); }
            public void UpdateRep() { Index = (uint)(Index < 7 ? 8 : 11); }
            public void UpdateShortRep() { Index = (uint)(Index < 7 ? 9 : 11); }
            public bool IsCharState() { return Index < 7; }
        }

        public const int kNumPosSlotBits = 6;
        public const int kDicLogSizeMin = 0;
        // public const int kDicLogSizeMax = 30;
        // public const uint kDistTableSizeMax = kDicLogSizeMax * 2;

        public const int kNumLenToPosStatesBits = 2; // it's for speed optimization
        public const uint kNumLenToPosStates = 1 << kNumLenToPosStatesBits;

        public const uint kMatchMinLen = 2;

        public static uint GetLenToPosState(uint len)
        {
            len -= kMatchMinLen;
            if (len < kNumLenToPosStates)
                return len;
            return (uint)(kNumLenToPosStates - 1);
        }

        public const int kNumAlignBits = 4;
        public const uint kAlignTableSize = 1 << kNumAlignBits;
        public const uint kAlignMask = (kAlignTableSize - 1);

        public const uint kStartPosModelIndex = 4;
        public const uint kEndPosModelIndex = 14;
        public const uint kNumPosModels = kEndPosModelIndex - kStartPosModelIndex;

        public const uint kNumFullDistances = 1 << ((int)kEndPosModelIndex / 2);

        public const uint kNumLitPosStatesBitsEncodingMax = 4;
        public const uint kNumLitContextBitsMax = 8;

        public const int kNumPosStatesBitsMax = 4;
        public const uint kNumPosStatesMax = (1 << kNumPosStatesBitsMax);
        public const int kNumPosStatesBitsEncodingMax = 4;
        public const uint kNumPosStatesEncodingMax = (1 << kNumPosStatesBitsEncodingMax);

        public const int kNumLowLenBits = 3;
        public const int kNumMidLenBits = 3;
        public const int kNumHighLenBits = 8;
        public const uint kNumLowLenSymbols = 1 << kNumLowLenBits;
        public const uint kNumMidLenSymbols = 1 << kNumMidLenBits;
        public const uint kNumLenSymbols = kNumLowLenSymbols + kNumMidLenSymbols +
                (1 << kNumHighLenBits);
        public const uint kMatchMaxLen = kMatchMinLen + kNumLenSymbols - 1;
    }

    interface IInWindowStream
    {
        void SetStream(System.IO.Stream inStream);
        void Init();
        void ReleaseStream();
        Byte GetIndexByte(Int32 index);
        UInt32 GetMatchLen(Int32 index, UInt32 distance, UInt32 limit);
        UInt32 GetNumAvailableBytes();
    }

    interface IMatchFinder : IInWindowStream
    {
        void Create(UInt32 historySize, UInt32 keepAddBufferBefore,
                UInt32 matchMaxLen, UInt32 keepAddBufferAfter);
        UInt32 GetMatches(UInt32[] distances);
        void Skip(UInt32 num);
    }

    class CRC
    {
        public static readonly uint[] Table;

        static CRC()
        {
            Table = new uint[256];
            const uint kPoly = 0xEDB88320;
            for (uint i = 0; i < 256; i++)
            {
                uint r = i;
                for (int j = 0; j < 8; j++)
                    if ((r & 1) != 0)
                        r = (r >> 1) ^ kPoly;
                    else
                        r >>= 1;
                Table[i] = r;
            }
        }

        uint _value = 0xFFFFFFFF;

        public void Init() { _value = 0xFFFFFFFF; }

        public void UpdateByte(byte b)
        {
            _value = Table[(((byte)(_value)) ^ b)] ^ (_value >> 8);
        }

        public void Update(byte[] data, uint offset, uint size)
        {
            for (uint i = 0; i < size; i++)
                _value = Table[(((byte)(_value)) ^ data[offset + i])] ^ (_value >> 8);
        }

        public uint GetDigest() { return _value ^ 0xFFFFFFFF; }

        static uint CalculateDigest(byte[] data, uint offset, uint size)
        {
            CRC crc = new CRC();
            // crc.Init();
            crc.Update(data, offset, size);
            return crc.GetDigest();
        }

        static bool VerifyDigest(uint digest, byte[] data, uint offset, uint size)
        {
            return (CalculateDigest(data, offset, size) == digest);
        }
    }

    class InWindow
    {
        public Byte[] _bufferBase = null; // pointer to buffer with data
        System.IO.Stream _stream;
        UInt32 _posLimit; // offset (from _buffer) of first byte when new block reading must be done
        bool _streamEndWasReached; // if (true) then _streamPos shows real end of stream

        UInt32 _pointerToLastSafePosition;

        public UInt32 _bufferOffset;

        public UInt32 _blockSize; // Size of Allocated memory block
        public UInt32 _pos; // offset (from _buffer) of curent byte
        UInt32 _keepSizeBefore; // how many BYTEs must be kept in buffer before _pos
        UInt32 _keepSizeAfter; // how many BYTEs must be kept buffer after _pos
        public UInt32 _streamPos; // offset (from _buffer) of first not read byte from Stream

        public void MoveBlock()
        {
            UInt32 offset = (UInt32)(_bufferOffset) + _pos - _keepSizeBefore;
            // we need one additional byte, since MovePos moves on 1 byte.
            if (offset > 0)
                offset--;

            UInt32 numBytes = (UInt32)(_bufferOffset) + _streamPos - offset;

            // check negative offset ????
            for (UInt32 i = 0; i < numBytes; i++)
                _bufferBase[i] = _bufferBase[offset + i];
            _bufferOffset -= offset;
        }

        public virtual void ReadBlock()
        {
            if (_streamEndWasReached)
                return;
            while (true)
            {
                int size = (int)((0 - _bufferOffset) + _blockSize - _streamPos);
                if (size == 0)
                    return;
                int numReadBytes = _stream.Read(_bufferBase, (int)(_bufferOffset + _streamPos), size);
                if (numReadBytes == 0)
                {
                    _posLimit = _streamPos;
                    UInt32 pointerToPostion = _bufferOffset + _posLimit;
                    if (pointerToPostion > _pointerToLastSafePosition)
                        _posLimit = (UInt32)(_pointerToLastSafePosition - _bufferOffset);

                    _streamEndWasReached = true;
                    return;
                }
                _streamPos += (UInt32)numReadBytes;
                if (_streamPos >= _pos + _keepSizeAfter)
                    _posLimit = _streamPos - _keepSizeAfter;
            }
        }

        void Free() { _bufferBase = null; }

        public void Create(UInt32 keepSizeBefore, UInt32 keepSizeAfter, UInt32 keepSizeReserv)
        {
            _keepSizeBefore = keepSizeBefore;
            _keepSizeAfter = keepSizeAfter;
            UInt32 blockSize = keepSizeBefore + keepSizeAfter + keepSizeReserv;
            if (_bufferBase == null || _blockSize != blockSize)
            {
                Free();
                _blockSize = blockSize;
                _bufferBase = new Byte[_blockSize];
            }
            _pointerToLastSafePosition = _blockSize - keepSizeAfter;
        }

        public void SetStream(System.IO.Stream stream) { _stream = stream; }
        public void ReleaseStream() { _stream = null; }

        public void Init()
        {
            _bufferOffset = 0;
            _pos = 0;
            _streamPos = 0;
            _streamEndWasReached = false;
            ReadBlock();
        }

        public void MovePos()
        {
            _pos++;
            if (_pos > _posLimit)
            {
                UInt32 pointerToPostion = _bufferOffset + _pos;
                if (pointerToPostion > _pointerToLastSafePosition)
                    MoveBlock();
                ReadBlock();
            }
        }

        public Byte GetIndexByte(Int32 index) { return _bufferBase[_bufferOffset + _pos + index]; }

        // index + limit have not to exceed _keepSizeAfter;
        public UInt32 GetMatchLen(Int32 index, UInt32 distance, UInt32 limit)
        {
            if (_streamEndWasReached)
                if ((_pos + index) + limit > _streamPos)
                    limit = _streamPos - (UInt32)(_pos + index);
            distance++;
            // Byte *pby = _buffer + (size_t)_pos + index;
            UInt32 pby = _bufferOffset + _pos + (UInt32)index;

            UInt32 i;
            for (i = 0; i < limit && _bufferBase[pby + i] == _bufferBase[pby + i - distance]; i++) ;
            return i;
        }

        public UInt32 GetNumAvailableBytes() { return _streamPos - _pos; }

        public void ReduceOffsets(Int32 subValue)
        {
            _bufferOffset += (UInt32)subValue;
            _posLimit -= (UInt32)subValue;
            _pos -= (UInt32)subValue;
            _streamPos -= (UInt32)subValue;
        }
    }

    class BinTree : InWindow, IMatchFinder
    {
        UInt32 _cyclicBufferPos;
        UInt32 _cyclicBufferSize = 0;
        UInt32 _matchMaxLen;

        UInt32[] _son;
        UInt32[] _hash;

        UInt32 _cutValue = 0xFF;
        UInt32 _hashMask;
        UInt32 _hashSizeSum = 0;

        bool HASH_ARRAY = true;

        const UInt32 kHash2Size = 1 << 10;
        const UInt32 kHash3Size = 1 << 16;
        const UInt32 kBT2HashSize = 1 << 16;
        const UInt32 kStartMaxLen = 1;
        const UInt32 kHash3Offset = kHash2Size;
        const UInt32 kEmptyHashValue = 0;
        const UInt32 kMaxValForNormalize = ((UInt32)1 << 31) - 1;

        UInt32 kNumHashDirectBytes = 0;
        UInt32 kMinMatchCheck = 4;
        UInt32 kFixHashSize = kHash2Size + kHash3Size;

        public void SetType(int numHashBytes)
        {
            HASH_ARRAY = (numHashBytes > 2);
            if (HASH_ARRAY)
            {
                kNumHashDirectBytes = 0;
                kMinMatchCheck = 4;
                kFixHashSize = kHash2Size + kHash3Size;
            }
            else
            {
                kNumHashDirectBytes = 2;
                kMinMatchCheck = 2 + 1;
                kFixHashSize = 0;
            }
        }

        public new void SetStream(System.IO.Stream stream) { base.SetStream(stream); }
        public new void ReleaseStream() { base.ReleaseStream(); }

        public new void Init()
        {
            base.Init();
            for (UInt32 i = 0; i < _hashSizeSum; i++)
                _hash[i] = kEmptyHashValue;
            _cyclicBufferPos = 0;
            ReduceOffsets(-1);
        }

        public new void MovePos()
        {
            if (++_cyclicBufferPos >= _cyclicBufferSize)
                _cyclicBufferPos = 0;
            base.MovePos();
            if (_pos == kMaxValForNormalize)
                Normalize();
        }

        public new Byte GetIndexByte(Int32 index) { return base.GetIndexByte(index); }

        public new UInt32 GetMatchLen(Int32 index, UInt32 distance, UInt32 limit)
        { return base.GetMatchLen(index, distance, limit); }

        public new UInt32 GetNumAvailableBytes() { return base.GetNumAvailableBytes(); }

        public void Create(UInt32 historySize, UInt32 keepAddBufferBefore,
                UInt32 matchMaxLen, UInt32 keepAddBufferAfter)
        {
            if (historySize > kMaxValForNormalize - 256)
                throw new Exception();
            _cutValue = 16 + (matchMaxLen >> 1);

            UInt32 windowReservSize = (historySize + keepAddBufferBefore +
                    matchMaxLen + keepAddBufferAfter) / 2 + 256;

            base.Create(historySize + keepAddBufferBefore, matchMaxLen + keepAddBufferAfter, windowReservSize);

            _matchMaxLen = matchMaxLen;

            UInt32 cyclicBufferSize = historySize + 1;
            if (_cyclicBufferSize != cyclicBufferSize)
                _son = new UInt32[(_cyclicBufferSize = cyclicBufferSize) * 2];

            UInt32 hs = kBT2HashSize;

            if (HASH_ARRAY)
            {
                hs = historySize - 1;
                hs |= (hs >> 1);
                hs |= (hs >> 2);
                hs |= (hs >> 4);
                hs |= (hs >> 8);
                hs >>= 1;
                hs |= 0xFFFF;
                if (hs > (1 << 24))
                    hs >>= 1;
                _hashMask = hs;
                hs++;
                hs += kFixHashSize;
            }
            if (hs != _hashSizeSum)
                _hash = new UInt32[_hashSizeSum = hs];
        }

        public UInt32 GetMatches(UInt32[] distances)
        {
            UInt32 lenLimit;
            if (_pos + _matchMaxLen <= _streamPos)
                lenLimit = _matchMaxLen;
            else
            {
                lenLimit = _streamPos - _pos;
                if (lenLimit < kMinMatchCheck)
                {
                    MovePos();
                    return 0;
                }
            }

            UInt32 offset = 0;
            UInt32 matchMinPos = (_pos > _cyclicBufferSize) ? (_pos - _cyclicBufferSize) : 0;
            UInt32 cur = _bufferOffset + _pos;
            UInt32 maxLen = kStartMaxLen; // to avoid items for len < hashSize;
            UInt32 hashValue, hash2Value = 0, hash3Value = 0;

            if (HASH_ARRAY)
            {
                UInt32 temp = CRC.Table[_bufferBase[cur]] ^ _bufferBase[cur + 1];
                hash2Value = temp & (kHash2Size - 1);
                temp ^= ((UInt32)(_bufferBase[cur + 2]) << 8);
                hash3Value = temp & (kHash3Size - 1);
                hashValue = (temp ^ (CRC.Table[_bufferBase[cur + 3]] << 5)) & _hashMask;
            }
            else
                hashValue = _bufferBase[cur] ^ ((UInt32)(_bufferBase[cur + 1]) << 8);

            UInt32 curMatch = _hash[kFixHashSize + hashValue];
            if (HASH_ARRAY)
            {
                UInt32 curMatch2 = _hash[hash2Value];
                UInt32 curMatch3 = _hash[kHash3Offset + hash3Value];
                _hash[hash2Value] = _pos;
                _hash[kHash3Offset + hash3Value] = _pos;
                if (curMatch2 > matchMinPos)
                    if (_bufferBase[_bufferOffset + curMatch2] == _bufferBase[cur])
                    {
                        distances[offset++] = maxLen = 2;
                        distances[offset++] = _pos - curMatch2 - 1;
                    }
                if (curMatch3 > matchMinPos)
                    if (_bufferBase[_bufferOffset + curMatch3] == _bufferBase[cur])
                    {
                        if (curMatch3 == curMatch2)
                            offset -= 2;
                        distances[offset++] = maxLen = 3;
                        distances[offset++] = _pos - curMatch3 - 1;
                        curMatch2 = curMatch3;
                    }
                if (offset != 0 && curMatch2 == curMatch)
                {
                    offset -= 2;
                    maxLen = kStartMaxLen;
                }
            }

            _hash[kFixHashSize + hashValue] = _pos;

            UInt32 ptr0 = (_cyclicBufferPos << 1) + 1;
            UInt32 ptr1 = (_cyclicBufferPos << 1);

            UInt32 len0, len1;
            len0 = len1 = kNumHashDirectBytes;

            if (kNumHashDirectBytes != 0)
            {
                if (curMatch > matchMinPos)
                {
                    if (_bufferBase[_bufferOffset + curMatch + kNumHashDirectBytes] !=
                            _bufferBase[cur + kNumHashDirectBytes])
                    {
                        distances[offset++] = maxLen = kNumHashDirectBytes;
                        distances[offset++] = _pos - curMatch - 1;
                    }
                }
            }

            UInt32 count = _cutValue;

            while (true)
            {
                if (curMatch <= matchMinPos || count-- == 0)
                {
                    _son[ptr0] = _son[ptr1] = kEmptyHashValue;
                    break;
                }
                UInt32 delta = _pos - curMatch;
                UInt32 cyclicPos = ((delta <= _cyclicBufferPos) ?
                            (_cyclicBufferPos - delta) :
                            (_cyclicBufferPos - delta + _cyclicBufferSize)) << 1;

                UInt32 pby1 = _bufferOffset + curMatch;
                UInt32 len = Math.Min(len0, len1);
                if (_bufferBase[pby1 + len] == _bufferBase[cur + len])
                {
                    while (++len != lenLimit)
                        if (_bufferBase[pby1 + len] != _bufferBase[cur + len])
                            break;
                    if (maxLen < len)
                    {
                        distances[offset++] = maxLen = len;
                        distances[offset++] = delta - 1;
                        if (len == lenLimit)
                        {
                            _son[ptr1] = _son[cyclicPos];
                            _son[ptr0] = _son[cyclicPos + 1];
                            break;
                        }
                    }
                }
                if (_bufferBase[pby1 + len] < _bufferBase[cur + len])
                {
                    _son[ptr1] = curMatch;
                    ptr1 = cyclicPos + 1;
                    curMatch = _son[ptr1];
                    len1 = len;
                }
                else
                {
                    _son[ptr0] = curMatch;
                    ptr0 = cyclicPos;
                    curMatch = _son[ptr0];
                    len0 = len;
                }
            }
            MovePos();
            return offset;
        }

        public void Skip(UInt32 num)
        {
            do
            {
                UInt32 lenLimit;
                if (_pos + _matchMaxLen <= _streamPos)
                    lenLimit = _matchMaxLen;
                else
                {
                    lenLimit = _streamPos - _pos;
                    if (lenLimit < kMinMatchCheck)
                    {
                        MovePos();
                        continue;
                    }
                }

                UInt32 matchMinPos = (_pos > _cyclicBufferSize) ? (_pos - _cyclicBufferSize) : 0;
                UInt32 cur = _bufferOffset + _pos;

                UInt32 hashValue;

                if (HASH_ARRAY)
                {
                    UInt32 temp = CRC.Table[_bufferBase[cur]] ^ _bufferBase[cur + 1];
                    UInt32 hash2Value = temp & (kHash2Size - 1);
                    _hash[hash2Value] = _pos;
                    temp ^= ((UInt32)(_bufferBase[cur + 2]) << 8);
                    UInt32 hash3Value = temp & (kHash3Size - 1);
                    _hash[kHash3Offset + hash3Value] = _pos;
                    hashValue = (temp ^ (CRC.Table[_bufferBase[cur + 3]] << 5)) & _hashMask;
                }
                else
                    hashValue = _bufferBase[cur] ^ ((UInt32)(_bufferBase[cur + 1]) << 8);

                UInt32 curMatch = _hash[kFixHashSize + hashValue];
                _hash[kFixHashSize + hashValue] = _pos;

                UInt32 ptr0 = (_cyclicBufferPos << 1) + 1;
                UInt32 ptr1 = (_cyclicBufferPos << 1);

                UInt32 len0, len1;
                len0 = len1 = kNumHashDirectBytes;

                UInt32 count = _cutValue;
                while (true)
                {
                    if (curMatch <= matchMinPos || count-- == 0)
                    {
                        _son[ptr0] = _son[ptr1] = kEmptyHashValue;
                        break;
                    }

                    UInt32 delta = _pos - curMatch;
                    UInt32 cyclicPos = ((delta <= _cyclicBufferPos) ?
                                (_cyclicBufferPos - delta) :
                                (_cyclicBufferPos - delta + _cyclicBufferSize)) << 1;

                    UInt32 pby1 = _bufferOffset + curMatch;
                    UInt32 len = Math.Min(len0, len1);
                    if (_bufferBase[pby1 + len] == _bufferBase[cur + len])
                    {
                        while (++len != lenLimit)
                            if (_bufferBase[pby1 + len] != _bufferBase[cur + len])
                                break;
                        if (len == lenLimit)
                        {
                            _son[ptr1] = _son[cyclicPos];
                            _son[ptr0] = _son[cyclicPos + 1];
                            break;
                        }
                    }
                    if (_bufferBase[pby1 + len] < _bufferBase[cur + len])
                    {
                        _son[ptr1] = curMatch;
                        ptr1 = cyclicPos + 1;
                        curMatch = _son[ptr1];
                        len1 = len;
                    }
                    else
                    {
                        _son[ptr0] = curMatch;
                        ptr0 = cyclicPos;
                        curMatch = _son[ptr0];
                        len0 = len;
                    }
                }
                MovePos();
            }
            while (--num != 0);
        }

        void NormalizeLinks(UInt32[] items, UInt32 numItems, UInt32 subValue)
        {
            for (UInt32 i = 0; i < numItems; i++)
            {
                UInt32 value = items[i];
                if (value <= subValue)
                    value = kEmptyHashValue;
                else
                    value -= subValue;
                items[i] = value;
            }
        }

        void Normalize()
        {
            UInt32 subValue = _pos - _cyclicBufferSize;
            NormalizeLinks(_son, _cyclicBufferSize * 2, subValue);
            NormalizeLinks(_hash, _hashSizeSum, subValue);
            ReduceOffsets((Int32)subValue);
        }

        public void SetCutValue(UInt32 cutValue) { _cutValue = cutValue; }
    }

    class Encoder
    {
        public const uint kTopValue = (1 << 24);

        System.IO.Stream Stream;

        public UInt64 Low;
        public uint Range;
        uint _cacheSize;
        byte _cache;

        long StartPosition;

        public void SetStream(System.IO.Stream stream)
        {
            Stream = stream;
        }

        public void ReleaseStream()
        {
            Stream = null;
        }

        public void Init()
        {
            StartPosition = Stream.Position;

            Low = 0;
            Range = 0xFFFFFFFF;
            _cacheSize = 1;
            _cache = 0;
        }

        public void FlushData()
        {
            for (int i = 0; i < 5; i++)
                ShiftLow();
        }

        public void FlushStream()
        {
            Stream.Flush();
        }

        public void CloseStream()
        {
            Stream.Close();
        }

        public void Encode(uint start, uint size, uint total)
        {
            Low += start * (Range /= total);
            Range *= size;
            while (Range < kTopValue)
            {
                Range <<= 8;
                ShiftLow();
            }
        }

        public void ShiftLow()
        {
            if ((uint)Low < (uint)0xFF000000 || (uint)(Low >> 32) == 1)
            {
                byte temp = _cache;
                do
                {
                    Stream.WriteByte((byte)(temp + (Low >> 32)));
                    temp = 0xFF;
                }
                while (--_cacheSize != 0);
                _cache = (byte)(((uint)Low) >> 24);
            }
            _cacheSize++;
            Low = ((uint)Low) << 8;
        }

        public void EncodeDirectBits(uint v, int numTotalBits)
        {
            for (int i = numTotalBits - 1; i >= 0; i--)
            {
                Range >>= 1;
                if (((v >> i) & 1) == 1)
                    Low += Range;
                if (Range < kTopValue)
                {
                    Range <<= 8;
                    ShiftLow();
                }
            }
        }

        public void EncodeBit(uint size0, int numTotalBits, uint symbol)
        {
            uint newBound = (Range >> numTotalBits) * size0;
            if (symbol == 0)
                Range = newBound;
            else
            {
                Low += newBound;
                Range -= newBound;
            }
            while (Range < kTopValue)
            {
                Range <<= 8;
                ShiftLow();
            }
        }

        public long GetProcessedSizeAdd()
        {
            return _cacheSize +
                Stream.Position - StartPosition + 4;
        }
    }

    struct BitEncoder
    {
        public const int kNumBitModelTotalBits = 11;
        public const uint kBitModelTotal = (1 << kNumBitModelTotalBits);
        const int kNumMoveBits = 5;
        const int kNumMoveReducingBits = 2;
        public const int kNumBitPriceShiftBits = 6;

        uint Prob;

        public void Init() { Prob = kBitModelTotal >> 1; }

        public void UpdateModel(uint symbol)
        {
            if (symbol == 0)
                Prob += (kBitModelTotal - Prob) >> kNumMoveBits;
            else
                Prob -= (Prob) >> kNumMoveBits;
        }

        public void Encode(Encoder encoder, uint symbol)
        {
            // encoder.EncodeBit(Prob, kNumBitModelTotalBits, symbol);
            // UpdateModel(symbol);
            uint newBound = (encoder.Range >> kNumBitModelTotalBits) * Prob;
            if (symbol == 0)
            {
                encoder.Range = newBound;
                Prob += (kBitModelTotal - Prob) >> kNumMoveBits;
            }
            else
            {
                encoder.Low += newBound;
                encoder.Range -= newBound;
                Prob -= (Prob) >> kNumMoveBits;
            }
            if (encoder.Range < Encoder.kTopValue)
            {
                encoder.Range <<= 8;
                encoder.ShiftLow();
            }
        }

        private static UInt32[] ProbPrices = new UInt32[kBitModelTotal >> kNumMoveReducingBits];

        static BitEncoder()
        {
            const int kNumBits = (kNumBitModelTotalBits - kNumMoveReducingBits);
            for (int i = kNumBits - 1; i >= 0; i--)
            {
                UInt32 start = (UInt32)1 << (kNumBits - i - 1);
                UInt32 end = (UInt32)1 << (kNumBits - i);
                for (UInt32 j = start; j < end; j++)
                    ProbPrices[j] = ((UInt32)i << kNumBitPriceShiftBits) +
                        (((end - j) << kNumBitPriceShiftBits) >> (kNumBits - i - 1));
            }
        }

        public uint GetPrice(uint symbol)
        {
            return ProbPrices[(((Prob - symbol) ^ ((-(int)symbol))) & (kBitModelTotal - 1)) >> kNumMoveReducingBits];
        }
        public uint GetPrice0() { return ProbPrices[Prob >> kNumMoveReducingBits]; }
        public uint GetPrice1() { return ProbPrices[(kBitModelTotal - Prob) >> kNumMoveReducingBits]; }
    }

    struct BitTreeEncoder
    {
        BitEncoder[] Models;
        int NumBitLevels;

        public BitTreeEncoder(int numBitLevels)
        {
            NumBitLevels = numBitLevels;
            Models = new BitEncoder[1 << numBitLevels];
        }

        public void Init()
        {
            for (uint i = 1; i < (1 << NumBitLevels); i++)
                Models[i].Init();
        }

        public void Encode(Encoder rangeEncoder, UInt32 symbol)
        {
            UInt32 m = 1;
            for (int bitIndex = NumBitLevels; bitIndex > 0;)
            {
                bitIndex--;
                UInt32 bit = (symbol >> bitIndex) & 1;
                Models[m].Encode(rangeEncoder, bit);
                m = (m << 1) | bit;
            }
        }

        public void ReverseEncode(Encoder rangeEncoder, UInt32 symbol)
        {
            UInt32 m = 1;
            for (UInt32 i = 0; i < NumBitLevels; i++)
            {
                UInt32 bit = symbol & 1;
                Models[m].Encode(rangeEncoder, bit);
                m = (m << 1) | bit;
                symbol >>= 1;
            }
        }

        public UInt32 GetPrice(UInt32 symbol)
        {
            UInt32 price = 0;
            UInt32 m = 1;
            for (int bitIndex = NumBitLevels; bitIndex > 0;)
            {
                bitIndex--;
                UInt32 bit = (symbol >> bitIndex) & 1;
                price += Models[m].GetPrice(bit);
                m = (m << 1) + bit;
            }
            return price;
        }

        public UInt32 ReverseGetPrice(UInt32 symbol)
        {
            UInt32 price = 0;
            UInt32 m = 1;
            for (int i = NumBitLevels; i > 0; i--)
            {
                UInt32 bit = symbol & 1;
                symbol >>= 1;
                price += Models[m].GetPrice(bit);
                m = (m << 1) | bit;
            }
            return price;
        }

        public static UInt32 ReverseGetPrice(BitEncoder[] Models, UInt32 startIndex,
            int NumBitLevels, UInt32 symbol)
        {
            UInt32 price = 0;
            UInt32 m = 1;
            for (int i = NumBitLevels; i > 0; i--)
            {
                UInt32 bit = symbol & 1;
                symbol >>= 1;
                price += Models[startIndex + m].GetPrice(bit);
                m = (m << 1) | bit;
            }
            return price;
        }

        public static void ReverseEncode(BitEncoder[] Models, UInt32 startIndex,
            Encoder rangeEncoder, int NumBitLevels, UInt32 symbol)
        {
            UInt32 m = 1;
            for (int i = 0; i < NumBitLevels; i++)
            {
                UInt32 bit = symbol & 1;
                Models[startIndex + m].Encode(rangeEncoder, bit);
                m = (m << 1) | bit;
                symbol >>= 1;
            }
        }
    }

    /// <summary>
    /// Provides the fields that represent properties idenitifiers for compressing.
    /// </summary>
    enum CoderPropID
    {
        /// <summary>
        /// Specifies default property.
        /// </summary>
        DefaultProp = 0,
        /// <summary>
        /// Specifies size of dictionary.
        /// </summary>
        DictionarySize,
        /// <summary>
        /// Specifies size of memory for PPM*.
        /// </summary>
        UsedMemorySize,
        /// <summary>
        /// Specifies order for PPM methods.
        /// </summary>
        Order,
        /// <summary>
        /// Specifies Block Size.
        /// </summary>
        BlockSize,
        /// <summary>
        /// Specifies number of postion state bits for LZMA (0 <= x <= 4).
        /// </summary>
        PosStateBits,
        /// <summary>
        /// Specifies number of literal context bits for LZMA (0 <= x <= 8).
        /// </summary>
        LitContextBits,
        /// <summary>
        /// Specifies number of literal position bits for LZMA (0 <= x <= 4).
        /// </summary>
        LitPosBits,
        /// <summary>
        /// Specifies number of fast bytes for LZ*.
        /// </summary>
        NumFastBytes,
        /// <summary>
        /// Specifies match finder. LZMA: "BT2", "BT4" or "BT4B".
        /// </summary>
        MatchFinder,
        /// <summary>
        /// Specifies the number of match finder cyckes.
        /// </summary>
        MatchFinderCycles,
        /// <summary>
        /// Specifies number of passes.
        /// </summary>
        NumPasses,
        /// <summary>
        /// Specifies number of algorithm.
        /// </summary>
        Algorithm,
        /// <summary>
        /// Specifies the number of threads.
        /// </summary>
        NumThreads,
        /// <summary>
        /// Specifies mode with end marker.
        /// </summary>
        EndMarker
    };

    class LZMAEncoder
    {
        enum EMatchFinderType
        {
            BT2,
            BT4,
        };

        const UInt32 kIfinityPrice = 0xFFFFFFF;

        static Byte[] g_FastPos = new Byte[1 << 11];

        static LZMAEncoder()
        {
            const Byte kFastSlots = 22;
            int c = 2;
            g_FastPos[0] = 0;
            g_FastPos[1] = 1;
            for (Byte slotFast = 2; slotFast < kFastSlots; slotFast++)
            {
                UInt32 k = ((UInt32)1 << ((slotFast >> 1) - 1));
                for (UInt32 j = 0; j < k; j++, c++)
                    g_FastPos[c] = slotFast;
            }
        }

        static UInt32 GetPosSlot(UInt32 pos)
        {
            if (pos < (1 << 11))
                return g_FastPos[pos];
            if (pos < (1 << 21))
                return (UInt32)(g_FastPos[pos >> 10] + 20);
            return (UInt32)(g_FastPos[pos >> 20] + 40);
        }

        static UInt32 GetPosSlot2(UInt32 pos)
        {
            if (pos < (1 << 17))
                return (UInt32)(g_FastPos[pos >> 6] + 12);
            if (pos < (1 << 27))
                return (UInt32)(g_FastPos[pos >> 16] + 32);
            return (UInt32)(g_FastPos[pos >> 26] + 52);
        }

        EncodeBase.State _state = new EncodeBase.State();
        Byte _previousByte;
        UInt32[] _repDistances = new UInt32[EncodeBase.kNumRepDistances];

        void BaseInit()
        {
            _state.Init();
            _previousByte = 0;
            for (UInt32 i = 0; i < EncodeBase.kNumRepDistances; i++)
                _repDistances[i] = 0;
        }

        const int kDefaultDictionaryLogSize = 22;
        const UInt32 kNumFastBytesDefault = 0x20;

        class LiteralEncoder
        {
            public struct Encoder2
            {
                BitEncoder[] m_Encoders;

                public void Create() { m_Encoders = new BitEncoder[0x300]; }

                public void Init() { for (int i = 0; i < 0x300; i++) m_Encoders[i].Init(); }

                public void Encode(Encoder rangeEncoder, byte symbol)
                {
                    uint context = 1;
                    for (int i = 7; i >= 0; i--)
                    {
                        uint bit = (uint)((symbol >> i) & 1);
                        m_Encoders[context].Encode(rangeEncoder, bit);
                        context = (context << 1) | bit;
                    }
                }

                public void EncodeMatched(Encoder rangeEncoder, byte matchByte, byte symbol)
                {
                    uint context = 1;
                    bool same = true;
                    for (int i = 7; i >= 0; i--)
                    {
                        uint bit = (uint)((symbol >> i) & 1);
                        uint state = context;
                        if (same)
                        {
                            uint matchBit = (uint)((matchByte >> i) & 1);
                            state += ((1 + matchBit) << 8);
                            same = (matchBit == bit);
                        }
                        m_Encoders[state].Encode(rangeEncoder, bit);
                        context = (context << 1) | bit;
                    }
                }

                public uint GetPrice(bool matchMode, byte matchByte, byte symbol)
                {
                    uint price = 0;
                    uint context = 1;
                    int i = 7;
                    if (matchMode)
                    {
                        for (; i >= 0; i--)
                        {
                            uint matchBit = (uint)(matchByte >> i) & 1;
                            uint bit = (uint)(symbol >> i) & 1;
                            price += m_Encoders[((1 + matchBit) << 8) + context].GetPrice(bit);
                            context = (context << 1) | bit;
                            if (matchBit != bit)
                            {
                                i--;
                                break;
                            }
                        }
                    }
                    for (; i >= 0; i--)
                    {
                        uint bit = (uint)(symbol >> i) & 1;
                        price += m_Encoders[context].GetPrice(bit);
                        context = (context << 1) | bit;
                    }
                    return price;
                }
            }

            Encoder2[] m_Coders;
            int m_NumPrevBits;
            int m_NumPosBits;
            uint m_PosMask;

            public void Create(int numPosBits, int numPrevBits)
            {
                if (m_Coders != null && m_NumPrevBits == numPrevBits && m_NumPosBits == numPosBits)
                    return;
                m_NumPosBits = numPosBits;
                m_PosMask = ((uint)1 << numPosBits) - 1;
                m_NumPrevBits = numPrevBits;
                uint numStates = (uint)1 << (m_NumPrevBits + m_NumPosBits);
                m_Coders = new Encoder2[numStates];
                for (uint i = 0; i < numStates; i++)
                    m_Coders[i].Create();
            }

            public void Init()
            {
                uint numStates = (uint)1 << (m_NumPrevBits + m_NumPosBits);
                for (uint i = 0; i < numStates; i++)
                    m_Coders[i].Init();
            }

            public Encoder2 GetSubCoder(UInt32 pos, Byte prevByte)
            { return m_Coders[((pos & m_PosMask) << m_NumPrevBits) + (uint)(prevByte >> (8 - m_NumPrevBits))]; }
        }

        class LenEncoder
        {
            BitEncoder _choice = new BitEncoder();
            BitEncoder _choice2 = new BitEncoder();
            BitTreeEncoder[] _lowCoder = new BitTreeEncoder[EncodeBase.kNumPosStatesEncodingMax];
            BitTreeEncoder[] _midCoder = new BitTreeEncoder[EncodeBase.kNumPosStatesEncodingMax];
            BitTreeEncoder _highCoder = new BitTreeEncoder(EncodeBase.kNumHighLenBits);

            public LenEncoder()
            {
                for (UInt32 posState = 0; posState < EncodeBase.kNumPosStatesEncodingMax; posState++)
                {
                    _lowCoder[posState] = new BitTreeEncoder(EncodeBase.kNumLowLenBits);
                    _midCoder[posState] = new BitTreeEncoder(EncodeBase.kNumMidLenBits);
                }
            }

            public void Init(UInt32 numPosStates)
            {
                _choice.Init();
                _choice2.Init();
                for (UInt32 posState = 0; posState < numPosStates; posState++)
                {
                    _lowCoder[posState].Init();
                    _midCoder[posState].Init();
                }
                _highCoder.Init();
            }

            public void Encode(Encoder rangeEncoder, UInt32 symbol, UInt32 posState)
            {
                if (symbol < EncodeBase.kNumLowLenSymbols)
                {
                    _choice.Encode(rangeEncoder, 0);
                    _lowCoder[posState].Encode(rangeEncoder, symbol);
                }
                else
                {
                    symbol -= EncodeBase.kNumLowLenSymbols;
                    _choice.Encode(rangeEncoder, 1);
                    if (symbol < EncodeBase.kNumMidLenSymbols)
                    {
                        _choice2.Encode(rangeEncoder, 0);
                        _midCoder[posState].Encode(rangeEncoder, symbol);
                    }
                    else
                    {
                        _choice2.Encode(rangeEncoder, 1);
                        _highCoder.Encode(rangeEncoder, symbol - EncodeBase.kNumMidLenSymbols);
                    }
                }
            }

            public void SetPrices(UInt32 posState, UInt32 numSymbols, UInt32[] prices, UInt32 st)
            {
                UInt32 a0 = _choice.GetPrice0();
                UInt32 a1 = _choice.GetPrice1();
                UInt32 b0 = a1 + _choice2.GetPrice0();
                UInt32 b1 = a1 + _choice2.GetPrice1();
                UInt32 i = 0;
                for (i = 0; i < EncodeBase.kNumLowLenSymbols; i++)
                {
                    if (i >= numSymbols)
                        return;
                    prices[st + i] = a0 + _lowCoder[posState].GetPrice(i);
                }
                for (; i < EncodeBase.kNumLowLenSymbols + EncodeBase.kNumMidLenSymbols; i++)
                {
                    if (i >= numSymbols)
                        return;
                    prices[st + i] = b0 + _midCoder[posState].GetPrice(i - EncodeBase.kNumLowLenSymbols);
                }
                for (; i < numSymbols; i++)
                    prices[st + i] = b1 + _highCoder.GetPrice(i - EncodeBase.kNumLowLenSymbols - EncodeBase.kNumMidLenSymbols);
            }
        };

        const UInt32 kNumLenSpecSymbols = EncodeBase.kNumLowLenSymbols + EncodeBase.kNumMidLenSymbols;

        class LenPriceTableEncoder : LenEncoder
        {
            UInt32[] _prices = new UInt32[EncodeBase.kNumLenSymbols << EncodeBase.kNumPosStatesBitsEncodingMax];
            UInt32 _tableSize;
            UInt32[] _counters = new UInt32[EncodeBase.kNumPosStatesEncodingMax];

            public void SetTableSize(UInt32 tableSize) { _tableSize = tableSize; }

            public UInt32 GetPrice(UInt32 symbol, UInt32 posState)
            {
                return _prices[posState * EncodeBase.kNumLenSymbols + symbol];
            }

            void UpdateTable(UInt32 posState)
            {
                SetPrices(posState, _tableSize, _prices, posState * EncodeBase.kNumLenSymbols);
                _counters[posState] = _tableSize;
            }

            public void UpdateTables(UInt32 numPosStates)
            {
                for (UInt32 posState = 0; posState < numPosStates; posState++)
                    UpdateTable(posState);
            }

            public new void Encode(Encoder rangeEncoder, UInt32 symbol, UInt32 posState)
            {
                base.Encode(rangeEncoder, symbol, posState);
                if (--_counters[posState] == 0)
                    UpdateTable(posState);
            }
        }

        const UInt32 kNumOpts = 1 << 12;
        class Optimal
        {
            public EncodeBase.State State;

            public bool Prev1IsChar;
            public bool Prev2;

            public UInt32 PosPrev2;
            public UInt32 BackPrev2;

            public UInt32 Price;
            public UInt32 PosPrev;
            public UInt32 BackPrev;

            public UInt32 Backs0;
            public UInt32 Backs1;
            public UInt32 Backs2;
            public UInt32 Backs3;

            public void MakeAsChar() { BackPrev = 0xFFFFFFFF; Prev1IsChar = false; }
            public void MakeAsShortRep() { BackPrev = 0; ; Prev1IsChar = false; }
            public bool IsShortRep() { return (BackPrev == 0); }
        };
        Optimal[] _optimum = new Optimal[kNumOpts];
        IMatchFinder _matchFinder = null;
        Encoder _rangeEncoder = new Encoder();

        BitEncoder[] _isMatch = new BitEncoder[EncodeBase.kNumStates << EncodeBase.kNumPosStatesBitsMax];
        BitEncoder[] _isRep = new BitEncoder[EncodeBase.kNumStates];
        BitEncoder[] _isRepG0 = new BitEncoder[EncodeBase.kNumStates];
        BitEncoder[] _isRepG1 = new BitEncoder[EncodeBase.kNumStates];
        BitEncoder[] _isRepG2 = new BitEncoder[EncodeBase.kNumStates];
        BitEncoder[] _isRep0Long = new BitEncoder[EncodeBase.kNumStates << EncodeBase.kNumPosStatesBitsMax];

        BitTreeEncoder[] _posSlotEncoder = new BitTreeEncoder[EncodeBase.kNumLenToPosStates];

        BitEncoder[] _posEncoders = new BitEncoder[EncodeBase.kNumFullDistances - EncodeBase.kEndPosModelIndex];
        BitTreeEncoder _posAlignEncoder = new BitTreeEncoder(EncodeBase.kNumAlignBits);

        LenPriceTableEncoder _lenEncoder = new LenPriceTableEncoder();
        LenPriceTableEncoder _repMatchLenEncoder = new LenPriceTableEncoder();

        LiteralEncoder _literalEncoder = new LiteralEncoder();

        UInt32[] _matchDistances = new UInt32[EncodeBase.kMatchMaxLen * 2 + 2];

        UInt32 _numFastBytes = kNumFastBytesDefault;
        UInt32 _longestMatchLength;
        UInt32 _numDistancePairs;

        UInt32 _additionalOffset;

        UInt32 _optimumEndIndex;
        UInt32 _optimumCurrentIndex;

        bool _longestMatchWasFound;

        UInt32[] _posSlotPrices = new UInt32[1 << (EncodeBase.kNumPosSlotBits + EncodeBase.kNumLenToPosStatesBits)];
        UInt32[] _distancesPrices = new UInt32[EncodeBase.kNumFullDistances << EncodeBase.kNumLenToPosStatesBits];
        UInt32[] _alignPrices = new UInt32[EncodeBase.kAlignTableSize];
        UInt32 _alignPriceCount;

        UInt32 _distTableSize = (kDefaultDictionaryLogSize * 2);

        int _posStateBits = 2;
        UInt32 _posStateMask = (4 - 1);
        int _numLiteralPosStateBits = 0;
        int _numLiteralContextBits = 3;

        UInt32 _dictionarySize = (1 << kDefaultDictionaryLogSize);
        UInt32 _dictionarySizePrev = 0xFFFFFFFF;
        UInt32 _numFastBytesPrev = 0xFFFFFFFF;

        Int64 nowPos64;
        bool _finished;
        System.IO.Stream _inStream;

        EMatchFinderType _matchFinderType = EMatchFinderType.BT4;
        bool _writeEndMark = false;

        bool _needReleaseMFStream;

        void Create()
        {
            if (_matchFinder == null)
            {
                BinTree bt = new BinTree();
                int numHashBytes = 4;
                if (_matchFinderType == EMatchFinderType.BT2)
                    numHashBytes = 2;
                bt.SetType(numHashBytes);
                _matchFinder = bt;
            }
            _literalEncoder.Create(_numLiteralPosStateBits, _numLiteralContextBits);

            if (_dictionarySize == _dictionarySizePrev && _numFastBytesPrev == _numFastBytes)
                return;
            _matchFinder.Create(_dictionarySize, kNumOpts, _numFastBytes, EncodeBase.kMatchMaxLen + 1);
            _dictionarySizePrev = _dictionarySize;
            _numFastBytesPrev = _numFastBytes;
        }

        public LZMAEncoder()
        {
            for (int i = 0; i < kNumOpts; i++)
                _optimum[i] = new Optimal();
            for (int i = 0; i < EncodeBase.kNumLenToPosStates; i++)
                _posSlotEncoder[i] = new BitTreeEncoder(EncodeBase.kNumPosSlotBits);
        }

        void SetWriteEndMarkerMode(bool writeEndMarker)
        {
            _writeEndMark = writeEndMarker;
        }

        void Init()
        {
            BaseInit();
            _rangeEncoder.Init();

            uint i;
            for (i = 0; i < EncodeBase.kNumStates; i++)
            {
                for (uint j = 0; j <= _posStateMask; j++)
                {
                    uint complexState = (i << EncodeBase.kNumPosStatesBitsMax) + j;
                    _isMatch[complexState].Init();
                    _isRep0Long[complexState].Init();
                }
                _isRep[i].Init();
                _isRepG0[i].Init();
                _isRepG1[i].Init();
                _isRepG2[i].Init();
            }
            _literalEncoder.Init();
            for (i = 0; i < EncodeBase.kNumLenToPosStates; i++)
                _posSlotEncoder[i].Init();
            for (i = 0; i < EncodeBase.kNumFullDistances - EncodeBase.kEndPosModelIndex; i++)
                _posEncoders[i].Init();

            _lenEncoder.Init((UInt32)1 << _posStateBits);
            _repMatchLenEncoder.Init((UInt32)1 << _posStateBits);

            _posAlignEncoder.Init();

            _longestMatchWasFound = false;
            _optimumEndIndex = 0;
            _optimumCurrentIndex = 0;
            _additionalOffset = 0;
        }

        void ReadMatchDistances(out UInt32 lenRes, out UInt32 numDistancePairs)
        {
            lenRes = 0;
            numDistancePairs = _matchFinder.GetMatches(_matchDistances);
            if (numDistancePairs > 0)
            {
                lenRes = _matchDistances[numDistancePairs - 2];
                if (lenRes == _numFastBytes)
                    lenRes += _matchFinder.GetMatchLen((int)lenRes - 1, _matchDistances[numDistancePairs - 1],
                        EncodeBase.kMatchMaxLen - lenRes);
            }
            _additionalOffset++;
        }


        void MovePos(UInt32 num)
        {
            if (num > 0)
            {
                _matchFinder.Skip(num);
                _additionalOffset += num;
            }
        }

        UInt32 GetRepLen1Price(EncodeBase.State state, UInt32 posState)
        {
            return _isRepG0[state.Index].GetPrice0() +
                    _isRep0Long[(state.Index << EncodeBase.kNumPosStatesBitsMax) + posState].GetPrice0();
        }

        UInt32 GetPureRepPrice(UInt32 repIndex, EncodeBase.State state, UInt32 posState)
        {
            UInt32 price;
            if (repIndex == 0)
            {
                price = _isRepG0[state.Index].GetPrice0();
                price += _isRep0Long[(state.Index << EncodeBase.kNumPosStatesBitsMax) + posState].GetPrice1();
            }
            else
            {
                price = _isRepG0[state.Index].GetPrice1();
                if (repIndex == 1)
                    price += _isRepG1[state.Index].GetPrice0();
                else
                {
                    price += _isRepG1[state.Index].GetPrice1();
                    price += _isRepG2[state.Index].GetPrice(repIndex - 2);
                }
            }
            return price;
        }

        UInt32 GetRepPrice(UInt32 repIndex, UInt32 len, EncodeBase.State state, UInt32 posState)
        {
            UInt32 price = _repMatchLenEncoder.GetPrice(len - EncodeBase.kMatchMinLen, posState);
            return price + GetPureRepPrice(repIndex, state, posState);
        }

        UInt32 GetPosLenPrice(UInt32 pos, UInt32 len, UInt32 posState)
        {
            UInt32 price;
            UInt32 lenToPosState = EncodeBase.GetLenToPosState(len);
            if (pos < EncodeBase.kNumFullDistances)
                price = _distancesPrices[(lenToPosState * EncodeBase.kNumFullDistances) + pos];
            else
                price = _posSlotPrices[(lenToPosState << EncodeBase.kNumPosSlotBits) + GetPosSlot2(pos)] +
                    _alignPrices[pos & EncodeBase.kAlignMask];
            return price + _lenEncoder.GetPrice(len - EncodeBase.kMatchMinLen, posState);
        }

        UInt32 Backward(out UInt32 backRes, UInt32 cur)
        {
            _optimumEndIndex = cur;
            UInt32 posMem = _optimum[cur].PosPrev;
            UInt32 backMem = _optimum[cur].BackPrev;
            do
            {
                if (_optimum[cur].Prev1IsChar)
                {
                    _optimum[posMem].MakeAsChar();
                    _optimum[posMem].PosPrev = posMem - 1;
                    if (_optimum[cur].Prev2)
                    {
                        _optimum[posMem - 1].Prev1IsChar = false;
                        _optimum[posMem - 1].PosPrev = _optimum[cur].PosPrev2;
                        _optimum[posMem - 1].BackPrev = _optimum[cur].BackPrev2;
                    }
                }
                UInt32 posPrev = posMem;
                UInt32 backCur = backMem;

                backMem = _optimum[posPrev].BackPrev;
                posMem = _optimum[posPrev].PosPrev;

                _optimum[posPrev].BackPrev = backCur;
                _optimum[posPrev].PosPrev = cur;
                cur = posPrev;
            }
            while (cur > 0);
            backRes = _optimum[0].BackPrev;
            _optimumCurrentIndex = _optimum[0].PosPrev;
            return _optimumCurrentIndex;
        }

        UInt32[] reps = new UInt32[EncodeBase.kNumRepDistances];
        UInt32[] repLens = new UInt32[EncodeBase.kNumRepDistances];


        UInt32 GetOptimum(UInt32 position, out UInt32 backRes)
        {
            if (_optimumEndIndex != _optimumCurrentIndex)
            {
                UInt32 lenRes = _optimum[_optimumCurrentIndex].PosPrev - _optimumCurrentIndex;
                backRes = _optimum[_optimumCurrentIndex].BackPrev;
                _optimumCurrentIndex = _optimum[_optimumCurrentIndex].PosPrev;
                return lenRes;
            }
            _optimumCurrentIndex = _optimumEndIndex = 0;

            UInt32 lenMain, numDistancePairs;
            if (!_longestMatchWasFound)
            {
                ReadMatchDistances(out lenMain, out numDistancePairs);
            }
            else
            {
                lenMain = _longestMatchLength;
                numDistancePairs = _numDistancePairs;
                _longestMatchWasFound = false;
            }

            UInt32 numAvailableBytes = _matchFinder.GetNumAvailableBytes() + 1;
            if (numAvailableBytes < 2)
            {
                backRes = 0xFFFFFFFF;
                return 1;
            }
            if (numAvailableBytes > EncodeBase.kMatchMaxLen)
                numAvailableBytes = EncodeBase.kMatchMaxLen;

            UInt32 repMaxIndex = 0;
            UInt32 i;
            for (i = 0; i < EncodeBase.kNumRepDistances; i++)
            {
                reps[i] = _repDistances[i];
                repLens[i] = _matchFinder.GetMatchLen(0 - 1, reps[i], EncodeBase.kMatchMaxLen);
                if (repLens[i] > repLens[repMaxIndex])
                    repMaxIndex = i;
            }
            if (repLens[repMaxIndex] >= _numFastBytes)
            {
                backRes = repMaxIndex;
                UInt32 lenRes = repLens[repMaxIndex];
                MovePos(lenRes - 1);
                return lenRes;
            }

            if (lenMain >= _numFastBytes)
            {
                backRes = _matchDistances[numDistancePairs - 1] + EncodeBase.kNumRepDistances;
                MovePos(lenMain - 1);
                return lenMain;
            }

            Byte currentByte = _matchFinder.GetIndexByte(0 - 1);
            Byte matchByte = _matchFinder.GetIndexByte((Int32)(0 - _repDistances[0] - 1 - 1));

            if (lenMain < 2 && currentByte != matchByte && repLens[repMaxIndex] < 2)
            {
                backRes = (UInt32)0xFFFFFFFF;
                return 1;
            }

            _optimum[0].State = _state;

            UInt32 posState = (position & _posStateMask);

            _optimum[1].Price = _isMatch[(_state.Index << EncodeBase.kNumPosStatesBitsMax) + posState].GetPrice0() +
                    _literalEncoder.GetSubCoder(position, _previousByte).GetPrice(!_state.IsCharState(), matchByte, currentByte);
            _optimum[1].MakeAsChar();

            UInt32 matchPrice = _isMatch[(_state.Index << EncodeBase.kNumPosStatesBitsMax) + posState].GetPrice1();
            UInt32 repMatchPrice = matchPrice + _isRep[_state.Index].GetPrice1();

            if (matchByte == currentByte)
            {
                UInt32 shortRepPrice = repMatchPrice + GetRepLen1Price(_state, posState);
                if (shortRepPrice < _optimum[1].Price)
                {
                    _optimum[1].Price = shortRepPrice;
                    _optimum[1].MakeAsShortRep();
                }
            }

            UInt32 lenEnd = ((lenMain >= repLens[repMaxIndex]) ? lenMain : repLens[repMaxIndex]);

            if (lenEnd < 2)
            {
                backRes = _optimum[1].BackPrev;
                return 1;
            }

            _optimum[1].PosPrev = 0;

            _optimum[0].Backs0 = reps[0];
            _optimum[0].Backs1 = reps[1];
            _optimum[0].Backs2 = reps[2];
            _optimum[0].Backs3 = reps[3];

            UInt32 len = lenEnd;
            do
                _optimum[len--].Price = kIfinityPrice;
            while (len >= 2);

            for (i = 0; i < EncodeBase.kNumRepDistances; i++)
            {
                UInt32 repLen = repLens[i];
                if (repLen < 2)
                    continue;
                UInt32 price = repMatchPrice + GetPureRepPrice(i, _state, posState);
                do
                {
                    UInt32 curAndLenPrice = price + _repMatchLenEncoder.GetPrice(repLen - 2, posState);
                    Optimal optimum = _optimum[repLen];
                    if (curAndLenPrice < optimum.Price)
                    {
                        optimum.Price = curAndLenPrice;
                        optimum.PosPrev = 0;
                        optimum.BackPrev = i;
                        optimum.Prev1IsChar = false;
                    }
                }
                while (--repLen >= 2);
            }

            UInt32 normalMatchPrice = matchPrice + _isRep[_state.Index].GetPrice0();

            len = ((repLens[0] >= 2) ? repLens[0] + 1 : 2);
            if (len <= lenMain)
            {
                UInt32 offs = 0;
                while (len > _matchDistances[offs])
                    offs += 2;
                for (; ; len++)
                {
                    UInt32 distance = _matchDistances[offs + 1];
                    UInt32 curAndLenPrice = normalMatchPrice + GetPosLenPrice(distance, len, posState);
                    Optimal optimum = _optimum[len];
                    if (curAndLenPrice < optimum.Price)
                    {
                        optimum.Price = curAndLenPrice;
                        optimum.PosPrev = 0;
                        optimum.BackPrev = distance + EncodeBase.kNumRepDistances;
                        optimum.Prev1IsChar = false;
                    }
                    if (len == _matchDistances[offs])
                    {
                        offs += 2;
                        if (offs == numDistancePairs)
                            break;
                    }
                }
            }

            UInt32 cur = 0;

            while (true)
            {
                cur++;
                if (cur == lenEnd)
                    return Backward(out backRes, cur);
                UInt32 newLen;
                ReadMatchDistances(out newLen, out numDistancePairs);
                if (newLen >= _numFastBytes)
                {
                    _numDistancePairs = numDistancePairs;
                    _longestMatchLength = newLen;
                    _longestMatchWasFound = true;
                    return Backward(out backRes, cur);
                }
                position++;
                UInt32 posPrev = _optimum[cur].PosPrev;
                EncodeBase.State state;
                if (_optimum[cur].Prev1IsChar)
                {
                    posPrev--;
                    if (_optimum[cur].Prev2)
                    {
                        state = _optimum[_optimum[cur].PosPrev2].State;
                        if (_optimum[cur].BackPrev2 < EncodeBase.kNumRepDistances)
                            state.UpdateRep();
                        else
                            state.UpdateMatch();
                    }
                    else
                        state = _optimum[posPrev].State;
                    state.UpdateChar();
                }
                else
                    state = _optimum[posPrev].State;
                if (posPrev == cur - 1)
                {
                    if (_optimum[cur].IsShortRep())
                        state.UpdateShortRep();
                    else
                        state.UpdateChar();
                }
                else
                {
                    UInt32 pos;
                    if (_optimum[cur].Prev1IsChar && _optimum[cur].Prev2)
                    {
                        posPrev = _optimum[cur].PosPrev2;
                        pos = _optimum[cur].BackPrev2;
                        state.UpdateRep();
                    }
                    else
                    {
                        pos = _optimum[cur].BackPrev;
                        if (pos < EncodeBase.kNumRepDistances)
                            state.UpdateRep();
                        else
                            state.UpdateMatch();
                    }
                    Optimal opt = _optimum[posPrev];
                    if (pos < EncodeBase.kNumRepDistances)
                    {
                        if (pos == 0)
                        {
                            reps[0] = opt.Backs0;
                            reps[1] = opt.Backs1;
                            reps[2] = opt.Backs2;
                            reps[3] = opt.Backs3;
                        }
                        else if (pos == 1)
                        {
                            reps[0] = opt.Backs1;
                            reps[1] = opt.Backs0;
                            reps[2] = opt.Backs2;
                            reps[3] = opt.Backs3;
                        }
                        else if (pos == 2)
                        {
                            reps[0] = opt.Backs2;
                            reps[1] = opt.Backs0;
                            reps[2] = opt.Backs1;
                            reps[3] = opt.Backs3;
                        }
                        else
                        {
                            reps[0] = opt.Backs3;
                            reps[1] = opt.Backs0;
                            reps[2] = opt.Backs1;
                            reps[3] = opt.Backs2;
                        }
                    }
                    else
                    {
                        reps[0] = (pos - EncodeBase.kNumRepDistances);
                        reps[1] = opt.Backs0;
                        reps[2] = opt.Backs1;
                        reps[3] = opt.Backs2;
                    }
                }
                _optimum[cur].State = state;
                _optimum[cur].Backs0 = reps[0];
                _optimum[cur].Backs1 = reps[1];
                _optimum[cur].Backs2 = reps[2];
                _optimum[cur].Backs3 = reps[3];
                UInt32 curPrice = _optimum[cur].Price;

                currentByte = _matchFinder.GetIndexByte(0 - 1);
                matchByte = _matchFinder.GetIndexByte((Int32)(0 - reps[0] - 1 - 1));

                posState = (position & _posStateMask);

                UInt32 curAnd1Price = curPrice +
                    _isMatch[(state.Index << EncodeBase.kNumPosStatesBitsMax) + posState].GetPrice0() +
                    _literalEncoder.GetSubCoder(position, _matchFinder.GetIndexByte(0 - 2)).
                    GetPrice(!state.IsCharState(), matchByte, currentByte);

                Optimal nextOptimum = _optimum[cur + 1];

                bool nextIsChar = false;
                if (curAnd1Price < nextOptimum.Price)
                {
                    nextOptimum.Price = curAnd1Price;
                    nextOptimum.PosPrev = cur;
                    nextOptimum.MakeAsChar();
                    nextIsChar = true;
                }

                matchPrice = curPrice + _isMatch[(state.Index << EncodeBase.kNumPosStatesBitsMax) + posState].GetPrice1();
                repMatchPrice = matchPrice + _isRep[state.Index].GetPrice1();

                if (matchByte == currentByte &&
                    !(nextOptimum.PosPrev < cur && nextOptimum.BackPrev == 0))
                {
                    UInt32 shortRepPrice = repMatchPrice + GetRepLen1Price(state, posState);
                    if (shortRepPrice <= nextOptimum.Price)
                    {
                        nextOptimum.Price = shortRepPrice;
                        nextOptimum.PosPrev = cur;
                        nextOptimum.MakeAsShortRep();
                        nextIsChar = true;
                    }
                }

                UInt32 numAvailableBytesFull = _matchFinder.GetNumAvailableBytes() + 1;
                numAvailableBytesFull = Math.Min(kNumOpts - 1 - cur, numAvailableBytesFull);
                numAvailableBytes = numAvailableBytesFull;

                if (numAvailableBytes < 2)
                    continue;
                if (numAvailableBytes > _numFastBytes)
                    numAvailableBytes = _numFastBytes;
                if (!nextIsChar && matchByte != currentByte)
                {
                    // try Literal + rep0
                    UInt32 t = Math.Min(numAvailableBytesFull - 1, _numFastBytes);
                    UInt32 lenTest2 = _matchFinder.GetMatchLen(0, reps[0], t);
                    if (lenTest2 >= 2)
                    {
                        EncodeBase.State state2 = state;
                        state2.UpdateChar();
                        UInt32 posStateNext = (position + 1) & _posStateMask;
                        UInt32 nextRepMatchPrice = curAnd1Price +
                            _isMatch[(state2.Index << EncodeBase.kNumPosStatesBitsMax) + posStateNext].GetPrice1() +
                            _isRep[state2.Index].GetPrice1();
                        {
                            UInt32 offset = cur + 1 + lenTest2;
                            while (lenEnd < offset)
                                _optimum[++lenEnd].Price = kIfinityPrice;
                            UInt32 curAndLenPrice = nextRepMatchPrice + GetRepPrice(
                                0, lenTest2, state2, posStateNext);
                            Optimal optimum = _optimum[offset];
                            if (curAndLenPrice < optimum.Price)
                            {
                                optimum.Price = curAndLenPrice;
                                optimum.PosPrev = cur + 1;
                                optimum.BackPrev = 0;
                                optimum.Prev1IsChar = true;
                                optimum.Prev2 = false;
                            }
                        }
                    }
                }

                UInt32 startLen = 2; // speed optimization 

                for (UInt32 repIndex = 0; repIndex < EncodeBase.kNumRepDistances; repIndex++)
                {
                    UInt32 lenTest = _matchFinder.GetMatchLen(0 - 1, reps[repIndex], numAvailableBytes);
                    if (lenTest < 2)
                        continue;
                    UInt32 lenTestTemp = lenTest;
                    do
                    {
                        while (lenEnd < cur + lenTest)
                            _optimum[++lenEnd].Price = kIfinityPrice;
                        UInt32 curAndLenPrice = repMatchPrice + GetRepPrice(repIndex, lenTest, state, posState);
                        Optimal optimum = _optimum[cur + lenTest];
                        if (curAndLenPrice < optimum.Price)
                        {
                            optimum.Price = curAndLenPrice;
                            optimum.PosPrev = cur;
                            optimum.BackPrev = repIndex;
                            optimum.Prev1IsChar = false;
                        }
                    }
                    while (--lenTest >= 2);
                    lenTest = lenTestTemp;

                    if (repIndex == 0)
                        startLen = lenTest + 1;

                    // if (_maxMode)
                    if (lenTest < numAvailableBytesFull)
                    {
                        UInt32 t = Math.Min(numAvailableBytesFull - 1 - lenTest, _numFastBytes);
                        UInt32 lenTest2 = _matchFinder.GetMatchLen((Int32)lenTest, reps[repIndex], t);
                        if (lenTest2 >= 2)
                        {
                            EncodeBase.State state2 = state;
                            state2.UpdateRep();
                            UInt32 posStateNext = (position + lenTest) & _posStateMask;
                            UInt32 curAndLenCharPrice =
                                    repMatchPrice + GetRepPrice(repIndex, lenTest, state, posState) +
                                    _isMatch[(state2.Index << EncodeBase.kNumPosStatesBitsMax) + posStateNext].GetPrice0() +
                                    _literalEncoder.GetSubCoder(position + lenTest,
                                    _matchFinder.GetIndexByte((Int32)lenTest - 1 - 1)).GetPrice(true,
                                    _matchFinder.GetIndexByte((Int32)((Int32)lenTest - 1 - (Int32)(reps[repIndex] + 1))),
                                    _matchFinder.GetIndexByte((Int32)lenTest - 1));
                            state2.UpdateChar();
                            posStateNext = (position + lenTest + 1) & _posStateMask;
                            UInt32 nextMatchPrice = curAndLenCharPrice + _isMatch[(state2.Index << EncodeBase.kNumPosStatesBitsMax) + posStateNext].GetPrice1();
                            UInt32 nextRepMatchPrice = nextMatchPrice + _isRep[state2.Index].GetPrice1();

                            // for(; lenTest2 >= 2; lenTest2--)
                            {
                                UInt32 offset = lenTest + 1 + lenTest2;
                                while (lenEnd < cur + offset)
                                    _optimum[++lenEnd].Price = kIfinityPrice;
                                UInt32 curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                                Optimal optimum = _optimum[cur + offset];
                                if (curAndLenPrice < optimum.Price)
                                {
                                    optimum.Price = curAndLenPrice;
                                    optimum.PosPrev = cur + lenTest + 1;
                                    optimum.BackPrev = 0;
                                    optimum.Prev1IsChar = true;
                                    optimum.Prev2 = true;
                                    optimum.PosPrev2 = cur;
                                    optimum.BackPrev2 = repIndex;
                                }
                            }
                        }
                    }
                }

                if (newLen > numAvailableBytes)
                {
                    newLen = numAvailableBytes;
                    for (numDistancePairs = 0; newLen > _matchDistances[numDistancePairs]; numDistancePairs += 2) ;
                    _matchDistances[numDistancePairs] = newLen;
                    numDistancePairs += 2;
                }
                if (newLen >= startLen)
                {
                    normalMatchPrice = matchPrice + _isRep[state.Index].GetPrice0();
                    while (lenEnd < cur + newLen)
                        _optimum[++lenEnd].Price = kIfinityPrice;

                    UInt32 offs = 0;
                    while (startLen > _matchDistances[offs])
                        offs += 2;

                    for (UInt32 lenTest = startLen; ; lenTest++)
                    {
                        UInt32 curBack = _matchDistances[offs + 1];
                        UInt32 curAndLenPrice = normalMatchPrice + GetPosLenPrice(curBack, lenTest, posState);
                        Optimal optimum = _optimum[cur + lenTest];
                        if (curAndLenPrice < optimum.Price)
                        {
                            optimum.Price = curAndLenPrice;
                            optimum.PosPrev = cur;
                            optimum.BackPrev = curBack + EncodeBase.kNumRepDistances;
                            optimum.Prev1IsChar = false;
                        }

                        if (lenTest == _matchDistances[offs])
                        {
                            if (lenTest < numAvailableBytesFull)
                            {
                                UInt32 t = Math.Min(numAvailableBytesFull - 1 - lenTest, _numFastBytes);
                                UInt32 lenTest2 = _matchFinder.GetMatchLen((Int32)lenTest, curBack, t);
                                if (lenTest2 >= 2)
                                {
                                    EncodeBase.State state2 = state;
                                    state2.UpdateMatch();
                                    UInt32 posStateNext = (position + lenTest) & _posStateMask;
                                    UInt32 curAndLenCharPrice = curAndLenPrice +
                                        _isMatch[(state2.Index << EncodeBase.kNumPosStatesBitsMax) + posStateNext].GetPrice0() +
                                        _literalEncoder.GetSubCoder(position + lenTest,
                                        _matchFinder.GetIndexByte((Int32)lenTest - 1 - 1)).
                                        GetPrice(true,
                                        _matchFinder.GetIndexByte((Int32)lenTest - (Int32)(curBack + 1) - 1),
                                        _matchFinder.GetIndexByte((Int32)lenTest - 1));
                                    state2.UpdateChar();
                                    posStateNext = (position + lenTest + 1) & _posStateMask;
                                    UInt32 nextMatchPrice = curAndLenCharPrice + _isMatch[(state2.Index << EncodeBase.kNumPosStatesBitsMax) + posStateNext].GetPrice1();
                                    UInt32 nextRepMatchPrice = nextMatchPrice + _isRep[state2.Index].GetPrice1();

                                    UInt32 offset = lenTest + 1 + lenTest2;
                                    while (lenEnd < cur + offset)
                                        _optimum[++lenEnd].Price = kIfinityPrice;
                                    curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                                    optimum = _optimum[cur + offset];
                                    if (curAndLenPrice < optimum.Price)
                                    {
                                        optimum.Price = curAndLenPrice;
                                        optimum.PosPrev = cur + lenTest + 1;
                                        optimum.BackPrev = 0;
                                        optimum.Prev1IsChar = true;
                                        optimum.Prev2 = true;
                                        optimum.PosPrev2 = cur;
                                        optimum.BackPrev2 = curBack + EncodeBase.kNumRepDistances;
                                    }
                                }
                            }
                            offs += 2;
                            if (offs == numDistancePairs)
                                break;
                        }
                    }
                }
            }
        }

        bool ChangePair(UInt32 smallDist, UInt32 bigDist)
        {
            const int kDif = 7;
            return (smallDist < ((UInt32)(1) << (32 - kDif)) && bigDist >= (smallDist << kDif));
        }

        void WriteEndMarker(UInt32 posState)
        {
            if (!_writeEndMark)
                return;

            _isMatch[(_state.Index << EncodeBase.kNumPosStatesBitsMax) + posState].Encode(_rangeEncoder, 1);
            _isRep[_state.Index].Encode(_rangeEncoder, 0);
            _state.UpdateMatch();
            UInt32 len = EncodeBase.kMatchMinLen;
            _lenEncoder.Encode(_rangeEncoder, len - EncodeBase.kMatchMinLen, posState);
            UInt32 posSlot = (1 << EncodeBase.kNumPosSlotBits) - 1;
            UInt32 lenToPosState = EncodeBase.GetLenToPosState(len);
            _posSlotEncoder[lenToPosState].Encode(_rangeEncoder, posSlot);
            int footerBits = 30;
            UInt32 posReduced = (((UInt32)1) << footerBits) - 1;
            _rangeEncoder.EncodeDirectBits(posReduced >> EncodeBase.kNumAlignBits, footerBits - EncodeBase.kNumAlignBits);
            _posAlignEncoder.ReverseEncode(_rangeEncoder, posReduced & EncodeBase.kAlignMask);
        }

        void Flush(UInt32 nowPos)
        {
            ReleaseMFStream();
            WriteEndMarker(nowPos & _posStateMask);
            _rangeEncoder.FlushData();
            _rangeEncoder.FlushStream();
        }

        public void CodeOneBlock(out Int64 inSize, out Int64 outSize, out bool finished)
        {
            inSize = 0;
            outSize = 0;
            finished = true;

            if (_inStream != null)
            {
                _matchFinder.SetStream(_inStream);
                _matchFinder.Init();
                _needReleaseMFStream = true;
                _inStream = null;
                if (_trainSize > 0)
                    _matchFinder.Skip(_trainSize);
            }

            if (_finished)
                return;
            _finished = true;


            Int64 progressPosValuePrev = nowPos64;
            if (nowPos64 == 0)
            {
                if (_matchFinder.GetNumAvailableBytes() == 0)
                {
                    Flush((UInt32)nowPos64);
                    return;
                }
                UInt32 len, numDistancePairs; // it's not used
                ReadMatchDistances(out len, out numDistancePairs);
                UInt32 posState = (UInt32)(nowPos64) & _posStateMask;
                _isMatch[(_state.Index << EncodeBase.kNumPosStatesBitsMax) + posState].Encode(_rangeEncoder, 0);
                _state.UpdateChar();
                Byte curByte = _matchFinder.GetIndexByte((Int32)(0 - _additionalOffset));
                _literalEncoder.GetSubCoder((UInt32)(nowPos64), _previousByte).Encode(_rangeEncoder, curByte);
                _previousByte = curByte;
                _additionalOffset--;
                nowPos64++;
            }
            if (_matchFinder.GetNumAvailableBytes() == 0)
            {
                Flush((UInt32)nowPos64);
                return;
            }
            while (true)
            {
                UInt32 pos;
                UInt32 len = GetOptimum((UInt32)nowPos64, out pos);

                UInt32 posState = ((UInt32)nowPos64) & _posStateMask;
                UInt32 complexState = (_state.Index << EncodeBase.kNumPosStatesBitsMax) + posState;
                if (len == 1 && pos == 0xFFFFFFFF)
                {
                    _isMatch[complexState].Encode(_rangeEncoder, 0);
                    Byte curByte = _matchFinder.GetIndexByte((Int32)(0 - _additionalOffset));
                    LiteralEncoder.Encoder2 subCoder = _literalEncoder.GetSubCoder((UInt32)nowPos64, _previousByte);
                    if (!_state.IsCharState())
                    {
                        Byte matchByte = _matchFinder.GetIndexByte((Int32)(0 - _repDistances[0] - 1 - _additionalOffset));
                        subCoder.EncodeMatched(_rangeEncoder, matchByte, curByte);
                    }
                    else
                        subCoder.Encode(_rangeEncoder, curByte);
                    _previousByte = curByte;
                    _state.UpdateChar();
                }
                else
                {
                    _isMatch[complexState].Encode(_rangeEncoder, 1);
                    if (pos < EncodeBase.kNumRepDistances)
                    {
                        _isRep[_state.Index].Encode(_rangeEncoder, 1);
                        if (pos == 0)
                        {
                            _isRepG0[_state.Index].Encode(_rangeEncoder, 0);
                            if (len == 1)
                                _isRep0Long[complexState].Encode(_rangeEncoder, 0);
                            else
                                _isRep0Long[complexState].Encode(_rangeEncoder, 1);
                        }
                        else
                        {
                            _isRepG0[_state.Index].Encode(_rangeEncoder, 1);
                            if (pos == 1)
                                _isRepG1[_state.Index].Encode(_rangeEncoder, 0);
                            else
                            {
                                _isRepG1[_state.Index].Encode(_rangeEncoder, 1);
                                _isRepG2[_state.Index].Encode(_rangeEncoder, pos - 2);
                            }
                        }
                        if (len == 1)
                            _state.UpdateShortRep();
                        else
                        {
                            _repMatchLenEncoder.Encode(_rangeEncoder, len - EncodeBase.kMatchMinLen, posState);
                            _state.UpdateRep();
                        }
                        UInt32 distance = _repDistances[pos];
                        if (pos != 0)
                        {
                            for (UInt32 i = pos; i >= 1; i--)
                                _repDistances[i] = _repDistances[i - 1];
                            _repDistances[0] = distance;
                        }
                    }
                    else
                    {
                        _isRep[_state.Index].Encode(_rangeEncoder, 0);
                        _state.UpdateMatch();
                        _lenEncoder.Encode(_rangeEncoder, len - EncodeBase.kMatchMinLen, posState);
                        pos -= EncodeBase.kNumRepDistances;
                        UInt32 posSlot = GetPosSlot(pos);
                        UInt32 lenToPosState = EncodeBase.GetLenToPosState(len);
                        _posSlotEncoder[lenToPosState].Encode(_rangeEncoder, posSlot);

                        if (posSlot >= EncodeBase.kStartPosModelIndex)
                        {
                            int footerBits = (int)((posSlot >> 1) - 1);
                            UInt32 baseVal = ((2 | (posSlot & 1)) << footerBits);
                            UInt32 posReduced = pos - baseVal;

                            if (posSlot < EncodeBase.kEndPosModelIndex)
                                BitTreeEncoder.ReverseEncode(_posEncoders,
                                        baseVal - posSlot - 1, _rangeEncoder, footerBits, posReduced);
                            else
                            {
                                _rangeEncoder.EncodeDirectBits(posReduced >> EncodeBase.kNumAlignBits, footerBits - EncodeBase.kNumAlignBits);
                                _posAlignEncoder.ReverseEncode(_rangeEncoder, posReduced & EncodeBase.kAlignMask);
                                _alignPriceCount++;
                            }
                        }
                        UInt32 distance = pos;
                        for (UInt32 i = EncodeBase.kNumRepDistances - 1; i >= 1; i--)
                            _repDistances[i] = _repDistances[i - 1];
                        _repDistances[0] = distance;
                        _matchPriceCount++;
                    }
                    _previousByte = _matchFinder.GetIndexByte((Int32)(len - 1 - _additionalOffset));
                }
                _additionalOffset -= len;
                nowPos64 += len;
                if (_additionalOffset == 0)
                {
                    // if (!_fastMode)
                    if (_matchPriceCount >= (1 << 7))
                        FillDistancesPrices();
                    if (_alignPriceCount >= EncodeBase.kAlignTableSize)
                        FillAlignPrices();
                    inSize = nowPos64;
                    outSize = _rangeEncoder.GetProcessedSizeAdd();
                    if (_matchFinder.GetNumAvailableBytes() == 0)
                    {
                        Flush((UInt32)nowPos64);
                        return;
                    }

                    if (nowPos64 - progressPosValuePrev >= (1 << 12))
                    {
                        _finished = false;
                        finished = false;
                        return;
                    }
                }
            }
        }

        void ReleaseMFStream()
        {
            if (_matchFinder != null && _needReleaseMFStream)
            {
                _matchFinder.ReleaseStream();
                _needReleaseMFStream = false;
            }
        }

        void SetOutStream(System.IO.Stream outStream) { _rangeEncoder.SetStream(outStream); }
        void ReleaseOutStream() { _rangeEncoder.ReleaseStream(); }

        void ReleaseStreams()
        {
            ReleaseMFStream();
            ReleaseOutStream();
        }

        void SetStreams(System.IO.Stream inStream, System.IO.Stream outStream,
                Int64 inSize, Int64 outSize)
        {
            _inStream = inStream;
            _finished = false;
            Create();
            SetOutStream(outStream);
            Init();

            // if (!_fastMode)
            {
                FillDistancesPrices();
                FillAlignPrices();
            }

            _lenEncoder.SetTableSize(_numFastBytes + 1 - EncodeBase.kMatchMinLen);
            _lenEncoder.UpdateTables((UInt32)1 << _posStateBits);
            _repMatchLenEncoder.SetTableSize(_numFastBytes + 1 - EncodeBase.kMatchMinLen);
            _repMatchLenEncoder.UpdateTables((UInt32)1 << _posStateBits);

            nowPos64 = 0;
        }


        public void Code(System.IO.Stream inStream, System.IO.Stream outStream,
            Int64 inSize, Int64 outSize)
        {
            _needReleaseMFStream = false;
            try
            {
                SetStreams(inStream, outStream, inSize, outSize);
                while (true)
                {
                    Int64 processedInSize;
                    Int64 processedOutSize;
                    bool finished;
                    CodeOneBlock(out processedInSize, out processedOutSize, out finished);
                    if (finished)
                        return;
                }
            }
            finally
            {
                ReleaseStreams();
            }
        }

        const int kPropSize = 5;
        Byte[] properties = new Byte[kPropSize];

        public void WriteCoderProperties(System.IO.Stream outStream)
        {
            properties[0] = (Byte)((_posStateBits * 5 + _numLiteralPosStateBits) * 9 + _numLiteralContextBits);
            for (int i = 0; i < 4; i++)
                properties[1 + i] = (Byte)((_dictionarySize >> (8 * i)) & 0xFF);
            outStream.Write(properties, 0, kPropSize);
        }

        UInt32[] tempPrices = new UInt32[EncodeBase.kNumFullDistances];
        UInt32 _matchPriceCount;

        void FillDistancesPrices()
        {
            for (UInt32 i = EncodeBase.kStartPosModelIndex; i < EncodeBase.kNumFullDistances; i++)
            {
                UInt32 posSlot = GetPosSlot(i);
                int footerBits = (int)((posSlot >> 1) - 1);
                UInt32 baseVal = ((2 | (posSlot & 1)) << footerBits);
                tempPrices[i] = BitTreeEncoder.ReverseGetPrice(_posEncoders,
                    baseVal - posSlot - 1, footerBits, i - baseVal);
            }

            for (UInt32 lenToPosState = 0; lenToPosState < EncodeBase.kNumLenToPosStates; lenToPosState++)
            {
                UInt32 posSlot;
                BitTreeEncoder encoder = _posSlotEncoder[lenToPosState];

                UInt32 st = (lenToPosState << EncodeBase.kNumPosSlotBits);
                for (posSlot = 0; posSlot < _distTableSize; posSlot++)
                    _posSlotPrices[st + posSlot] = encoder.GetPrice(posSlot);
                for (posSlot = EncodeBase.kEndPosModelIndex; posSlot < _distTableSize; posSlot++)
                    _posSlotPrices[st + posSlot] += ((((posSlot >> 1) - 1) - EncodeBase.kNumAlignBits) << BitEncoder.kNumBitPriceShiftBits);

                UInt32 st2 = lenToPosState * EncodeBase.kNumFullDistances;
                UInt32 i;
                for (i = 0; i < EncodeBase.kStartPosModelIndex; i++)
                    _distancesPrices[st2 + i] = _posSlotPrices[st + i];
                for (; i < EncodeBase.kNumFullDistances; i++)
                    _distancesPrices[st2 + i] = _posSlotPrices[st + GetPosSlot(i)] + tempPrices[i];
            }
            _matchPriceCount = 0;
        }

        void FillAlignPrices()
        {
            for (UInt32 i = 0; i < EncodeBase.kAlignTableSize; i++)
                _alignPrices[i] = _posAlignEncoder.ReverseGetPrice(i);
            _alignPriceCount = 0;
        }


        static string[] kMatchFinderIDs =
        {
            "BT2",
            "BT4",
        };

        static int FindMatchFinder(string s)
        {
            for (int m = 0; m < kMatchFinderIDs.Length; m++)
                if (s == kMatchFinderIDs[m])
                    return m;
            return -1;
        }

        public void SetCoderProperties(CoderPropID[] propIDs, object[] properties)
        {
            for (UInt32 i = 0; i < properties.Length; i++)
            {
                object prop = properties[i];
                switch (propIDs[i])
                {
                    case CoderPropID.NumFastBytes:
                        {
                            if (!(prop is Int32))
                                throw new InvalidOperationException();
                            Int32 numFastBytes = (Int32)prop;
                            if (numFastBytes < 5 || numFastBytes > EncodeBase.kMatchMaxLen)
                                throw new InvalidOperationException();
                            _numFastBytes = (UInt32)numFastBytes;
                            break;
                        }
                    case CoderPropID.Algorithm:
                        {
                            /*
                            if (!(prop is Int32))
                                throw new InvalidOperationException();
                            Int32 maximize = (Int32)prop;
                            _fastMode = (maximize == 0);
                            _maxMode = (maximize >= 2);
                            */
                            break;
                        }
                    case CoderPropID.MatchFinder:
                        {
                            if (!(prop is String))
                                throw new InvalidOperationException();
                            EMatchFinderType matchFinderIndexPrev = _matchFinderType;
                            int m = FindMatchFinder(((string)prop).ToUpper());
                            if (m < 0)
                                throw new InvalidOperationException();
                            _matchFinderType = (EMatchFinderType)m;
                            if (_matchFinder != null && matchFinderIndexPrev != _matchFinderType)
                            {
                                _dictionarySizePrev = 0xFFFFFFFF;
                                _matchFinder = null;
                            }
                            break;
                        }
                    case CoderPropID.DictionarySize:
                        {
                            const int kDicLogSizeMaxCompress = 30;
                            if (!(prop is Int32))
                                throw new InvalidOperationException(); ;
                            Int32 dictionarySize = (Int32)prop;
                            if (dictionarySize < (UInt32)(1 << EncodeBase.kDicLogSizeMin) ||
                                dictionarySize > (UInt32)(1 << kDicLogSizeMaxCompress))
                                throw new InvalidOperationException();
                            _dictionarySize = (UInt32)dictionarySize;
                            int dicLogSize;
                            for (dicLogSize = 0; dicLogSize < (UInt32)kDicLogSizeMaxCompress; dicLogSize++)
                                if (dictionarySize <= ((UInt32)(1) << dicLogSize))
                                    break;
                            _distTableSize = (UInt32)dicLogSize * 2;
                            break;
                        }
                    case CoderPropID.PosStateBits:
                        {
                            if (!(prop is Int32))
                                throw new InvalidOperationException();
                            Int32 v = (Int32)prop;
                            if (v < 0 || v > (UInt32)EncodeBase.kNumPosStatesBitsEncodingMax)
                                throw new InvalidOperationException();
                            _posStateBits = (int)v;
                            _posStateMask = (((UInt32)1) << (int)_posStateBits) - 1;
                            break;
                        }
                    case CoderPropID.LitPosBits:
                        {
                            if (!(prop is Int32))
                                throw new InvalidOperationException();
                            Int32 v = (Int32)prop;
                            if (v < 0 || v > (UInt32)EncodeBase.kNumLitPosStatesBitsEncodingMax)
                                throw new InvalidOperationException();
                            _numLiteralPosStateBits = (int)v;
                            break;
                        }
                    case CoderPropID.LitContextBits:
                        {
                            if (!(prop is Int32))
                                throw new InvalidOperationException();
                            Int32 v = (Int32)prop;
                            if (v < 0 || v > (UInt32)EncodeBase.kNumLitContextBitsMax)
                                throw new InvalidOperationException(); ;
                            _numLiteralContextBits = (int)v;
                            break;
                        }
                    case CoderPropID.EndMarker:
                        {
                            if (!(prop is Boolean))
                                throw new InvalidOperationException();
                            SetWriteEndMarkerMode((Boolean)prop);
                            break;
                        }
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        uint _trainSize = 0;
        public void SetTrainSize(uint trainSize)
        {
            _trainSize = trainSize;
        }

    }
}
