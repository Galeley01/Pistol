using NUnit.Framework;
using Pistol.Core;

namespace Pistol.Tests
{
    // Random choice of the cylinder close method: by weight, never more than N of the same in a row
    public class WeightedPickTests
    {
        [Test]
        public void PicksByWeight()
        {
            var w = new[] { 1f, 3f };
            Assert.That(WeightedPick.Pick(w, -1, 0, 0, 0.00f), Is.EqualTo(0));
            Assert.That(WeightedPick.Pick(w, -1, 0, 0, 0.24f), Is.EqualTo(0));
            Assert.That(WeightedPick.Pick(w, -1, 0, 0, 0.26f), Is.EqualTo(1));
            Assert.That(WeightedPick.Pick(w, -1, 0, 0, 0.9999f), Is.EqualTo(1));
        }

        [Test]
        public void ZeroWeightIsNeverPicked_AndAllZeroGivesNone()
        {
            var w = new[] { 0f, 1f };
            for (float r = 0f; r < 1f; r += 0.05f)
                Assert.That(WeightedPick.Pick(w, -1, 0, 2, r), Is.EqualTo(1));
            Assert.That(WeightedPick.Pick(new[] { 0f, 0f }, -1, 0, 2, 0.5f), Is.EqualTo(-1));
        }

        [Test]
        public void NeverMoreThanTwoSameInARow()
        {
            var w = new[] { 1f, 1f };
            var rng = new System.Random(7);
            int last = -1, run = 0;
            for (int i = 0; i < 2000; i++)
            {
                int pick = WeightedPick.Pick(w, last, run, 2, (float)rng.NextDouble());
                run = WeightedPick.NextRun(pick, last, run);
                last = pick;
                Assert.That(run, Is.LessThanOrEqualTo(2));
            }
        }

        [Test]
        public void AfterTwoSame_TheThirdIsTheOther()
        {
            var w = new[] { 1f, 1f };
            Assert.That(WeightedPick.Pick(w, 0, 2, 2, 0.0f), Is.EqualTo(1));
            Assert.That(WeightedPick.Pick(w, 1, 2, 2, 0.99f), Is.EqualTo(0));
        }

        [Test]
        public void SingleOptionRepeatsDespiteTheLimit()
        {
            var w = new[] { 1f };
            Assert.That(WeightedPick.Pick(w, 0, 5, 2, 0.5f), Is.EqualTo(0));
        }
    }
}
