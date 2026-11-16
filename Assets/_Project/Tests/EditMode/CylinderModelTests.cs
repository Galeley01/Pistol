using NUnit.Framework;
using UnityEngine;
using Pistol.Ballistics;
using Pistol.Weapons.Revolver;

namespace Pistol.Tests
{
    // Rules of the revolver cylinder (CylinderModel): loading, firing, dry clicks, rotation.
    // Run them in Window > General > Test Runner > EditMode > Run All.
    public class CylinderModelTests
    {
        AmmoType ammo;
        CylinderModel model;

        [SetUp]
        public void SetUp()
        {
            ammo = ScriptableObject.CreateInstance<AmmoType>();
            model = new CylinderModel();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(ammo);

        [Test]
        public void NewCylinder_IsEmpty_WithChamberZeroUnderHammer()
        {
            Assert.AreEqual(CylinderModel.Count, model.CountOf(RoundState.Empty));
            Assert.AreEqual(0, model.UnderHammer);
        }

        [Test]
        public void FillAll_LoadsSixLiveRounds()
        {
            model.FillAll(ammo);
            Assert.AreEqual(6, model.CountOf(RoundState.Live));
            Assert.AreSame(ammo, model.GetAmmo(3));
        }

        [Test]
        public void Fire_LiveRound_LeavesSpentCase_AndRaisesFired()
        {
            model.FillAll(ammo);
            int firedChamber = -1;
            model.Fired += (i, a) => firedChamber = i;

            bool fired = model.TryFire(out var firedAmmo);

            Assert.IsTrue(fired);
            Assert.AreSame(ammo, firedAmmo);
            Assert.AreEqual(0, firedChamber);
            Assert.AreEqual(RoundState.Spent, model.GetState(0));
        }

        [Test]
        public void Fire_SpentCase_IsDryClick()
        {
            model.Set(0, RoundState.Spent, ammo);
            RoundState clicked = RoundState.Live;
            model.DryFired += (i, s) => clicked = s;

            Assert.IsFalse(model.TryFire(out var firedAmmo));
            Assert.IsNull(firedAmmo);
            Assert.AreEqual(RoundState.Spent, clicked);
        }

        [Test]
        public void Fire_EmptyChamber_IsDryClick()
        {
            bool clicked = false;
            model.DryFired += (i, s) => clicked = true;
            Assert.IsFalse(model.TryFire(out _));
            Assert.IsTrue(clicked);
        }

        [Test]
        public void Advance_WrapsAroundBothWays()
        {
            model.Advance(7);
            Assert.AreEqual(1, model.UnderHammer);
            model.Advance(-2);
            Assert.AreEqual(5, model.UnderHammer);
        }

        [Test]
        public void SixShots_EmptyAFullCylinder_SeventhClicks()
        {
            model.FillAll(ammo);
            for (int shot = 0; shot < 6; shot++)
            {
                model.Advance();   // double action: the cylinder turns before the hammer falls
                Assert.IsTrue(model.TryFire(out _), $"shot {shot + 1}");
            }
            Assert.AreEqual(6, model.CountOf(RoundState.Spent));
            model.Advance();
            Assert.IsFalse(model.TryFire(out _));
        }

        [Test]
        public void Insert_OnlyIntoEmptyChamber()
        {
            Assert.IsTrue(model.Insert(2, ammo));
            Assert.IsFalse(model.Insert(2, ammo), "chamber already has a round");
            Assert.AreEqual(RoundState.Live, model.GetState(2));
        }

        [Test]
        public void RemoveSpent_OnlyRemovesSpentCases()
        {
            model.Insert(1, ammo);
            Assert.IsFalse(model.RemoveSpent(1), "a live round is not a spent case");
            model.Set(1, RoundState.Spent, ammo);
            Assert.IsTrue(model.RemoveSpent(1));
            Assert.AreEqual(RoundState.Empty, model.GetState(1));
            Assert.IsNull(model.GetAmmo(1));
        }

        [Test]
        public void SpendAll_KeepsEmptyChambersEmpty()
        {
            model.Insert(0, ammo);
            model.Insert(4, ammo);
            model.SpendAll();
            Assert.AreEqual(2, model.CountOf(RoundState.Spent));
            Assert.AreEqual(4, model.CountOf(RoundState.Empty));
        }

        [Test]
        public void Describe_MarksTheChamberUnderTheHammer()
        {
            model.FillAll(ammo);
            model.TryFire(out _);
            StringAssert.StartsWith("[○]", model.Describe());
        }
    }
}
