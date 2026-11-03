using System;
using System.Collections.Generic;

namespace RoadToPro.MatchEngine.Match.Events
{
    /// <summary>
    /// 틱 동안 단계가 낸 이벤트를 모았다가 정렬 키 순으로 로그에 넣고 구독자에게 전달한다 (specs/006 FR-01 ⑩, FR-32·35).
    /// 대기열은 고정 배열, 정렬은 삽입 정렬(안정, 할당 0). 단계 안 순서(Order)는 발생 순으로 자동 부여.
    /// </summary>
    public sealed class EventPublisher
    {
        public const int MaxPendingPerTick = 512;

        readonly SimEvent[] _pending = new SimEvent[MaxPendingPerTick];
        readonly ushort[] _orderByStage = new ushort[SimEvent.FlowStage + 1];
        readonly List<ISimEventSubscriber> _subs = new List<ISimEventSubscriber>();
        int _count;

        public int Pending => _count;
        public int SubscriberFaults { get; private set; }
        public Exception LastSubscriberFault { get; private set; }

        public void Subscribe(ISimEventSubscriber s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            _subs.Add(s);
        }

        public void Unsubscribe(ISimEventSubscriber s) => _subs.Remove(s);

        public ref readonly SimEvent PendingAt(int i) => ref _pending[i];

        /// <summary>단계 stage가 틱 tick에 낸 이벤트. Stage·Order·Tick은 여기서 채운다.</summary>
        public void Emit(long tick, byte stage, SimEvent e)
        {
            if (_count == _pending.Length) throw new InvalidOperationException($"틱당 이벤트 {MaxPendingPerTick}건 초과");
            if (stage >= _orderByStage.Length) throw new ArgumentOutOfRangeException(nameof(stage));
            e.Tick = tick;
            e.Stage = stage;
            e.Order = _orderByStage[stage]++;
            _pending[_count++] = e;
        }

        /// <summary>정렬해 로그에 넣고 구독자에게 보낸다. 대기열을 비운다.</summary>
        public void Flush(SimEventLog log, IMatchView view)
        {
            // 삽입 정렬 (안정): 대부분 이미 거의 정렬돼 있다
            for (int i = 1; i < _count; i++)
            {
                var x = _pending[i];
                int j = i - 1;
                while (j >= 0 && SimEvent.CompareKey(in _pending[j], in x) > 0)
                {
                    _pending[j + 1] = _pending[j];
                    j--;
                }
                _pending[j + 1] = x;
            }
            for (int i = 0; i < _count; i++)
            {
                log.Append(ref _pending[i]);
                for (int k = 0; k < _subs.Count; k++)
                {
                    try
                    {
                        _subs[k].OnEvent(in _pending[i], view);
                    }
                    catch (Exception ex)
                    {
                        SubscriberFaults++;
                        LastSubscriberFault = ex;
                    }
                }
            }
            Clear();
        }

        public void Clear()
        {
            _count = 0;
            Array.Clear(_orderByStage, 0, _orderByStage.Length);
        }
    }
}
