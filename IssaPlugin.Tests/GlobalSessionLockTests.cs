using IssaPlugin.Items;
using NUnit.Framework;

namespace IssaPlugin.Tests
{
    /// <summary>
    /// Tests for GlobalSessionLock&lt;T&gt;.
    ///
    /// GlobalSessionLock uses C# generic statics, meaning each closed type
    /// (GlobalSessionLock&lt;FakeA&gt;, GlobalSessionLock&lt;FakeB&gt;) owns its own
    /// independent _active / _holder fields. Tests exploit this to run in
    /// isolation without needing any Unity or Mirror infrastructure.
    ///
    /// Because the fields are static, each test must call Release() in
    /// [TearDown] to avoid state leaking into subsequent tests.
    /// </summary>
    [TestFixture]
    public class GlobalSessionLockTests
    {
        // ── Distinct sentinel types so tests never share a lock slot ──────────

        private class SlotA { }

        private class SlotB { }

        private class SlotC { }

        private class SlotD { }

        private class SlotE { }

        private class SlotF { }

        private class SlotG { }

        private class SlotH { }

        private class SlotI { }

        [TearDown]
        public void TearDown()
        {
            // Always clean up every slot used in this file so no state bleeds
            // between tests regardless of execution order.
            GlobalSessionLock<SlotA>.Release();
            GlobalSessionLock<SlotB>.Release();
            GlobalSessionLock<SlotC>.Release();
            GlobalSessionLock<SlotD>.Release();
            GlobalSessionLock<SlotE>.Release();
            GlobalSessionLock<SlotF>.Release();
            GlobalSessionLock<SlotG>.Release();
            GlobalSessionLock<SlotH>.Release();
            GlobalSessionLock<SlotI>.Release();
        }

        // ── Initial state ─────────────────────────────────────────────────────

        [Test]
        public void InitialState_IsNotActive()
        {
            // A fresh (never-touched) slot should report inactive.
            // We use a unique type so nothing else in the suite can have touched it.
            Assert.That(GlobalSessionLock<SlotA>.IsActive, Is.False);
        }

        [Test]
        public void InitialState_HolderIsNull()
        {
            Assert.That(GlobalSessionLock<SlotB>.Holder, Is.Null);
        }

        // ── TryAcquire ────────────────────────────────────────────────────────

        [Test]
        public void TryAcquire_WhenFree_ReturnsTrue()
        {
            var instance = new SlotC();
            Assert.That(GlobalSessionLock<SlotC>.TryAcquire(instance), Is.True);
        }

        [Test]
        public void TryAcquire_WhenFree_SetsIsActive()
        {
            var instance = new SlotD();
            GlobalSessionLock<SlotD>.TryAcquire(instance);
            Assert.That(GlobalSessionLock<SlotD>.IsActive, Is.True);
        }

        [Test]
        public void TryAcquire_WhenFree_SetsHolder()
        {
            var instance = new SlotE();
            GlobalSessionLock<SlotE>.TryAcquire(instance);
            Assert.That(GlobalSessionLock<SlotE>.Holder, Is.SameAs(instance));
        }

        [Test]
        public void TryAcquire_WhenAlreadyHeld_ReturnsFalse()
        {
            var first = new SlotF();
            var second = new SlotF();
            GlobalSessionLock<SlotF>.TryAcquire(first);

            Assert.That(GlobalSessionLock<SlotF>.TryAcquire(second), Is.False);
        }

        [Test]
        public void TryAcquire_WhenAlreadyHeld_DoesNotChangeHolder()
        {
            var first = new SlotG();
            var second = new SlotG();
            GlobalSessionLock<SlotG>.TryAcquire(first);
            GlobalSessionLock<SlotG>.TryAcquire(second);

            // The original holder must be unchanged.
            Assert.That(GlobalSessionLock<SlotG>.Holder, Is.SameAs(first));
        }

        // ── Release ───────────────────────────────────────────────────────────

        [Test]
        public void Release_ClearsIsActive()
        {
            var instance = new SlotH();
            GlobalSessionLock<SlotH>.TryAcquire(instance);
            GlobalSessionLock<SlotH>.Release();

            Assert.That(GlobalSessionLock<SlotH>.IsActive, Is.False);
        }

        [Test]
        public void Release_ClearsHolder()
        {
            var instance = new SlotA();
            GlobalSessionLock<SlotA>.TryAcquire(instance);
            GlobalSessionLock<SlotA>.Release();

            Assert.That(GlobalSessionLock<SlotA>.Holder, Is.Null);
        }

        [Test]
        public void Release_WithoutAcquire_IsIdempotent()
        {
            // Calling Release on a slot that was never acquired must not throw
            // and must leave the slot in a clean (inactive) state.
            Assert.DoesNotThrow(() => GlobalSessionLock<SlotB>.Release());
            Assert.That(GlobalSessionLock<SlotB>.IsActive, Is.False);
        }

        [Test]
        public void Release_CalledTwice_IsIdempotent()
        {
            var instance = new SlotC();
            GlobalSessionLock<SlotC>.TryAcquire(instance);
            GlobalSessionLock<SlotC>.Release();

            Assert.DoesNotThrow(() => GlobalSessionLock<SlotC>.Release());
            Assert.That(GlobalSessionLock<SlotC>.IsActive, Is.False);
        }

        // ── Acquire-after-release cycle ───────────────────────────────────────

        [Test]
        public void TryAcquire_AfterRelease_Succeeds()
        {
            var first = new SlotD();
            var second = new SlotD();

            GlobalSessionLock<SlotD>.TryAcquire(first);
            GlobalSessionLock<SlotD>.Release();

            // The lock should now be free; a new instance must be able to claim it.
            Assert.That(GlobalSessionLock<SlotD>.TryAcquire(second), Is.True);
            Assert.That(GlobalSessionLock<SlotD>.Holder, Is.SameAs(second));
        }

        // ── Type independence (the core generic-statics invariant) ────────────

        [Test]
        public void DifferentClosedTypes_HaveIndependentState()
        {
            var holderE = new SlotE();
            var holderF = new SlotF();

            GlobalSessionLock<SlotE>.TryAcquire(holderE);

            // Acquiring a different slot must succeed even though SlotE is held.
            bool acquiredF = GlobalSessionLock<SlotF>.TryAcquire(holderF);

            Assert.That(
                acquiredF,
                Is.True,
                "Acquiring SlotF should succeed while SlotE is already held."
            );
            Assert.That(
                GlobalSessionLock<SlotE>.IsActive,
                Is.True,
                "SlotE must still be active after SlotF is acquired."
            );
            Assert.That(
                GlobalSessionLock<SlotF>.IsActive,
                Is.True,
                "SlotF must be active after acquisition."
            );
        }

        [Test]
        public void ReleasingOneType_DoesNotAffectAnother()
        {
            var holderG = new SlotG();
            var holderH = new SlotH();

            GlobalSessionLock<SlotG>.TryAcquire(holderG);
            GlobalSessionLock<SlotH>.TryAcquire(holderH);

            GlobalSessionLock<SlotG>.Release();

            Assert.That(
                GlobalSessionLock<SlotG>.IsActive,
                Is.False,
                "SlotG must be inactive after release."
            );
            Assert.That(
                GlobalSessionLock<SlotH>.IsActive,
                Is.True,
                "SlotH must remain active — a sibling release must not affect it."
            );
            Assert.That(
                GlobalSessionLock<SlotH>.Holder,
                Is.SameAs(holderH),
                "SlotH holder must be unchanged after SlotG is released."
            );
        }

        // ── Holder identity ───────────────────────────────────────────────────

        [Test]
        public void Holder_IsExactSameReference_NotACopy()
        {
            // Value-type boxing edge-case guard: SlotI is a class, so reference
            // equality is the right check, but make it explicit.
            var instance = new SlotI();
            GlobalSessionLock<SlotI>.TryAcquire(instance);

            Assert.That(ReferenceEquals(GlobalSessionLock<SlotI>.Holder, instance), Is.True);
        }
    }
}
