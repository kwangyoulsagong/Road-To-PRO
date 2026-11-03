using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.Core.Serialization
{
    /// <summary>
    /// <see cref="StateWriter"/>가 쓴 바이트를 읽는다 (specs/006 FR-60). 끝을 넘거나 섹션 태그·길이가 맞지 않으면 <see cref="FormatException"/>.
    /// </summary>
    public sealed class StateReader
    {
        byte[] _buf;
        int _pos, _end;
        int _sectionEnd = -1;

        public StateReader() { }

        public StateReader(byte[] data) => Reset(data, data?.Length ?? 0);

        public void Reset(byte[] data, int length)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (length < 0 || length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));
            _buf = data;
            _pos = 0;
            _end = length;
            _sectionEnd = -1;
        }

        public int Position => _pos;
        public int Remaining => _end - _pos;
        public bool AtEnd => _pos == _end;

        public void BeginSection(byte tag)
        {
            if (_sectionEnd >= 0) throw new InvalidOperationException("섹션은 중첩할 수 없다");
            var t = ReadByte();
            if (t != tag) throw new FormatException($"섹션 태그 불일치: 기대 {tag}, 실제 {t} (위치 {_pos - 1})");
            var len = ReadUInt();
            if (len > (uint)(_end - _pos)) throw new FormatException($"섹션 {tag} 길이 {len}가 남은 데이터 {_end - _pos}를 넘는다");
            _sectionEnd = _pos + (int)len;
        }

        public void EndSection()
        {
            if (_sectionEnd < 0) throw new InvalidOperationException("열린 섹션이 없다");
            if (_pos != _sectionEnd) throw new FormatException($"섹션을 다 읽지 않았다 (남음 {_sectionEnd - _pos} B) — 형식 불일치");
            _sectionEnd = -1;
        }

        public byte ReadByte()
        {
            Need(1);
            return _buf[_pos++];
        }

        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        public bool ReadBool()
        {
            var b = ReadByte();
            if (b > 1) throw new FormatException($"bool 값이 0/1이 아니다: {b}");
            return b == 1;
        }

        public ushort ReadUShort()
        {
            Need(2);
            var v = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8));
            _pos += 2;
            return v;
        }

        public uint ReadUInt()
        {
            Need(4);
            var v = (uint)(_buf[_pos] | (_buf[_pos + 1] << 8) | (_buf[_pos + 2] << 16) | (_buf[_pos + 3] << 24));
            _pos += 4;
            return v;
        }

        public int ReadInt() => unchecked((int)ReadUInt());

        public ulong ReadULong()
        {
            Need(8);
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)_buf[_pos + i] << (8 * i);
            _pos += 8;
            return v;
        }

        public long ReadLong() => unchecked((long)ReadULong());

        public float ReadFloat() => BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt()));
        public double ReadDouble() => BitConverter.Int64BitsToDouble(unchecked((long)ReadULong()));

        public Vec2 ReadVec2()
        {
            var x = ReadFloat();
            var y = ReadFloat();
            return new Vec2(x, y);
        }

        public Vec3 ReadVec3()
        {
            var x = ReadFloat();
            var y = ReadFloat();
            var z = ReadFloat();
            return new Vec3(x, y, z);
        }

        public RngState ReadRngState()
        {
            Need(RngState.ByteSize);
            var s = RngState.ReadFrom(new ReadOnlySpan<byte>(_buf, _pos, RngState.ByteSize));
            _pos += RngState.ByteSize;
            return s;
        }

        void Need(int n)
        {
            var limit = _sectionEnd >= 0 ? _sectionEnd : _end;
            if (_buf == null) throw new InvalidOperationException("Reset으로 데이터를 넣지 않았다");
            if (_pos + n > limit) throw new FormatException($"데이터 끝을 넘어 읽음 (위치 {_pos}, 요청 {n} B, 한계 {limit})");
        }
    }
}
