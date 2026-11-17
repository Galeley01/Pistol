using NUnit.Framework;
using UnityEngine;
using Pistol.Core;

namespace Pistol.Tests
{
    // The shape of one shot's recoil (RecoilShape): rise, one nod below the start, slow settle.
    public class RecoilShapeTests
    {
        static readonly RecoilShape Shape = new RecoilShape(0.035f, 0.12f, 0.3f, 0.25f);

        [Test]
        public void Peaks_AtTheEndOfTheRise()
        {
            Assert.That(Shape.WithNod(0f), Is.EqualTo(0f));
            Assert.That(Shape.WithNod(Shape.Rise), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Shape.WithNod(Shape.Rise * 0.5f), Is.InRange(0.5f, 1f));
        }

        [Test]
        public void Nods_BelowTheStartByTheNodDepth()
        {
            float bottom = Shape.WithNod(Shape.Rise + Shape.Fall);
            Assert.That(bottom, Is.EqualTo(-Shape.Nod).Within(1e-4f));
        }

        [Test]
        public void CrossesZeroOnce_ThenSettlesWithoutSwinging()
        {
            // Count changes of the last clearly non-zero sign
            int signChanges = 0;
            float lastSign = 1f;
            float lowest = 0f;
            for (float t = 0.001f; t <= Shape.Duration + 0.05f; t += 0.001f)
            {
                float v = Shape.WithNod(t);
                lowest = Mathf.Min(lowest, v);
                if (Mathf.Abs(v) < 1e-5f) continue;
                float sign = Mathf.Sign(v);
                if (sign != lastSign) { signChanges++; lastSign = sign; }
            }
            Assert.That(signChanges, Is.EqualTo(1));
            Assert.That(lowest, Is.GreaterThanOrEqualTo(-Shape.Nod - 1e-4f));
            Assert.That(Shape.WithNod(Shape.Duration), Is.EqualTo(0f));
        }

        [Test]
        public void WithoutNod_NeverGoesBelowZero()
        {
            for (float t = 0f; t <= Shape.Duration; t += 0.001f)
                Assert.That(Shape.WithoutNod(t), Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void SettleBump_OnlyWhileLevelling()
        {
            Assert.That(Shape.SettleBump(Shape.Rise), Is.EqualTo(0f));
            Assert.That(Shape.SettleBump(Shape.Rise + Shape.Fall + Shape.Settle * 0.5f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Shape.SettleBump(Shape.Duration + 0.01f), Is.EqualTo(0f));
        }

        [Test]
        public void ZeroNod_ReturnsWithoutOvershoot()
        {
            var flat = new RecoilShape(0.035f, 0.12f, 0.3f, 0f);
            for (float t = 0f; t <= flat.Duration; t += 0.001f)
                Assert.That(flat.WithNod(t), Is.GreaterThanOrEqualTo(0f));
        }
    }
}
