using NUnit.Framework;
using UnityEngine;
using Pistol.Ballistics;

namespace Pistol.Tests
{
    // The bullet flight step (BulletMath) shared by real bullets and shot previews.
    public class BulletMathTests
    {
        static readonly Vector3 Gravity = new Vector3(0f, -9.81f, 0f);

        static Vector3 Fly(Vector3 pos, Vector3 vel, float time, int steps)
        {
            float dt = time / steps;
            for (int i = 0; i < steps; i++) BulletMath.Step(pos, vel, Gravity, dt, out pos, out vel);
            return pos;
        }

        [Test]
        public void Step_MatchesTheExactFormula()
        {
            var start = new Vector3(1f, 2f, 3f);
            var vel = new Vector3(0f, 5f, 440f);
            const float t = 0.5f;
            Vector3 exact = start + vel * t + 0.5f * Gravity * t * t;

            Vector3 pos = Fly(start, vel, t, 30);

            Assert.That(Vector3.Distance(pos, exact), Is.LessThan(0.001f));
        }

        [Test]
        public void Path_DoesNotDependOnTheStepSize()
        {
            var vel = new Vector3(0f, 0f, 440f);

            Vector3 coarse = Fly(Vector3.zero, vel, 1f, 10);    // like 10 fps
            Vector3 fine = Fly(Vector3.zero, vel, 1f, 240);     // like 240 fps

            Assert.That(Vector3.Distance(coarse, fine), Is.LessThan(0.001f));
        }

        [Test]
        public void Step_UpdatesTheVelocity()
        {
            BulletMath.Step(Vector3.zero, new Vector3(0f, 0f, 100f), Gravity, 0.1f, out _, out Vector3 vel);

            Assert.That(vel.z, Is.EqualTo(100f).Within(1e-4f));
            Assert.That(vel.y, Is.EqualTo(-0.981f).Within(1e-4f));
        }
    }
}
