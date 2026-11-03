using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.Core.Serialization
{
    /// <summary>
    /// 시뮬 상태 바이너리 기록기 (specs/006 FR-60). 리틀엔디언, float은 비트 그대로(무손실).
    /// 버퍼를 재사용하므로 <see cref="Reset"/> 후 다시 쓰면 할당이 없다 (용량이 모자랄 때만 2배로 늘림).
    /// 섹션: <see cref="BeginSection"/>·<see cref="EndSection"/>이 [태그 u8][길이 u32]를 남겨 판독기가 누락·초과를 잡는다.
    /// </summary>
    public sealed class StateWriter
    {
        byte[] _buf;
        int _pos;
        int _sectionStart = -1;

        public StateWriter(int initialCapacity = 16 * 1024)
        {
            if (initialCapacity < 16) initialCapacity = 16;
            _buf = new byte[initialCapacity];
        }

        public int Length => _pos;
        public byte[] Buffer => _buf;

        public void Reset()
        {
            _pos = 0;
            _sectionStart = -1;
        }

        public ReadOnlySpan<byte> Written => new ReadOnlySpan<byte>(_buf, 0, _pos);

        public byte[] ToArray()
        {
            var a = new byte[_pos];
            System.Buffer.BlockCopy(_buf, 0, a, 0, _pos);
            return a;
        }

        public void BeginSection(byte tag)
        {
            if (_sectionStart >= 0) throw new InvalidOperationException("섹션은 중첩할 수 없다");
            Write(tag);
            _sectionStart = _pos;
            Write(0u);
        }

        public void EndSection()
        {
            if (_sectionStart < 0) throw new InvalidOperationException("열린 섹션이 없다");
            var len = (uint)(_pos - _sectionStart - 4);
            PutU32(_sectionStart, len);
            _sectionStart = -1;
        }

        public void Write(byte v)
        {
            Ensure(1);
            _buf[_pos++] = v;
        }

        public void Write(sbyte v) => Write(unchecked((byte)v));
        public void Write(bool v) => Write(v ? (byte)1 : (byte)0);

        public void Write(ushort v)
        {
            Ensure(2);
            _buf[_pos++] = (byte)v;
            _buf[_pos++] = (byte)(v >> 8);
        }

        public void Write(uint v)
        {
            Ensure(4);
            PutU32(_pos, v);
            _pos += 4;
        }

        public void Write(int v) => Write(unchecked((uint)v));

        public void Write(ulong v)
        {
            Ensure(8);
            for (int i = 0; i < 8; i++) _buf[_pos++] = (byte)(v >> (8 * i));
        }

        public void Write(long v) => Write(unchecked((ulong)v));

        /// <summary>비트 그대로 (−0·NaN 페이로드 포함 무손실).</summary>
        public void Write(float v) => Write(unchecked((uint)BitConverter.SingleToInt32Bits(v)));
        public void Write(double v) => Write(unchecked((ulong)BitConverter.DoubleToInt64Bits(v)));

        public void Write(Vec2 v)
        {
            Write(v.X);
            Write(v.Y);
        }

        public void Write(Vec3 v)
        {
            Write(v.X);
            Write(v.Y);
            Write(v.Z);
        }

        public void Write(RngState s)
        {
            Ensure(RngState.ByteSize);
            s.WriteTo(new Span<byte>(_buf, _pos, RngState.ByteSize));
            _pos += RngState.ByteSize;
        }

        void PutU32(int at, uint v)
        {
            _buf[at] = (byte)v;
            _buf[at + 1] = (byte)(v >> 8);
            _buf[at + 2] = (byte)(v >> 16);
            _buf[at + 3] = (byte)(v >> 24);
        }

        void Ensure(int n)
        {
            if (_pos + n <= _buf.Length) return;
            var cap = _buf.Length * 2;
            while (cap < _pos + n) cap *= 2;
            Array.Resize(ref _buf, cap);
        }
    }
}
