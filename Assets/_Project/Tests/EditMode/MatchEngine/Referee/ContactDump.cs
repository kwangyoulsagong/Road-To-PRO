using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 T29 진단 (판정 없음): 실경기 12경기 접촉 상세를 `.farm/contacts.csv`로 — 파울 계수 오프라인 보정용.</summary>
    public class ContactDump
    {
        [Test, Explicit("진단 (012 T29)"), Category("Slow")]
        public void Dump()
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("match,kind,intensity,behind,studs,height,late,ballFirst,sliding,master,inBox,dogsoValid,goalDist,defenders,control\n");
            var sims = LiveReferee.Played();
            for (int m = 0; m < sims.Length; m++)
            {
                var log = sims[m].Context.Actions.Details.Contacts;
                for (int i = 0; i < log.Count; i++)
                {
                    if (!log.IsAvailable(i)) continue;
                    var x = log[i];
                    sb.Append(m).Append(',').Append((int)x.Kind).Append(',').Append(x.Intensity.ToString(c)).Append(',').Append(x.FromBehind.ToString(c)).Append(',')
                      .Append(x.StudsUp.ToString(c)).Append(',').Append(x.Height.ToString(c)).Append(',').Append(x.Lateness.ToString(c)).Append(',').Append(x.BallFirst.ToString(c)).Append(',')
                      .Append(x.Sliding ? 1 : 0).Append(',').Append((x.HabitBits & ContactDetail.HabitSlideMaster) != 0 ? 1 : 0).Append(',').Append(x.InBox ? 1 : 0).Append(',')
                      .Append(x.DogsoValid ? 1 : 0).Append(',').Append(x.GoalDist.ToString(c)).Append(',').Append(x.DefendersBetween).Append(',').Append(x.ControlLikely.ToString(c)).Append('\n');
                }
            }
            var dir = Path.Combine(Directory.GetCurrentDirectory(), ".farm");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "contacts.csv"), sb.ToString());
        }
    }
}
