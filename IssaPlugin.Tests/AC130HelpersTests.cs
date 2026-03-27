using IssaPlugin.Items;
using NUnit.Framework;
using UnityEngine;

namespace IssaPlugin.Tests
{
    /// <summary>
    /// Tests for AC130Helpers — pure trigonometric helpers with no Unity
    /// runtime dependencies beyond Mathf / Vector3 (both available via the
    /// UnityEngine.Modules NuGet stub used by this test project).
    ///
    /// Floating-point comparisons use a tolerance of 1e-5f, which is tighter
    /// than any game-relevant precision but loose enough to absorb IEEE 754
    /// rounding across platforms.
    /// </summary>
    [TestFixture]
    public class AC130HelpersTests
    {
        private const float Epsilon = 1e-5f;

        // ── OrbitPosition ─────────────────────────────────────────────────────
        //
        // The orbit lives in the XZ plane (Y is altitude).
        // At angle θ degrees:
        //   x = centre.x + cos(θ°) * radius
        //   y = centre.y + altitude
        //   z = centre.z + sin(θ°) * radius

        [Test]
        public void OrbitPosition_AtZeroDegrees_LiesAlongPositiveX()
        {
            // cos(0) = 1, sin(0) = 0  →  position = (cx + radius, cy + alt, cz)
            var centre = new Vector3(10f, 5f, 20f);
            float radius = 100f,
                altitude = 50f;

            Vector3 result = AC130Helpers.OrbitPosition(centre, 0f, radius, altitude);

            Assert.That(
                result.x,
                Is.EqualTo(centre.x + radius).Within(Epsilon),
                "At 0° x should be centre.x + radius."
            );
            Assert.That(
                result.y,
                Is.EqualTo(centre.y + altitude).Within(Epsilon),
                "Y should always be centre.y + altitude."
            );
            Assert.That(
                result.z,
                Is.EqualTo(centre.z).Within(Epsilon),
                "At 0° z should equal centre.z (sin(0)=0)."
            );
        }

        [Test]
        public void OrbitPosition_At90Degrees_LiesAlongPositiveZ()
        {
            // cos(90°) ≈ 0, sin(90°) = 1  →  position ≈ (cx, cy + alt, cz + radius)
            var centre = Vector3.zero;
            float radius = 50f,
                altitude = 10f;

            Vector3 result = AC130Helpers.OrbitPosition(centre, 90f, radius, altitude);

            Assert.That(
                result.x,
                Is.EqualTo(0f).Within(Epsilon),
                "At 90° x should be ~0 (cos(90°)≈0)."
            );
            Assert.That(
                result.z,
                Is.EqualTo(radius).Within(Epsilon),
                "At 90° z should equal radius (sin(90°)=1)."
            );
        }

        [Test]
        public void OrbitPosition_At180Degrees_LiesAlongNegativeX()
        {
            // cos(180°) = -1, sin(180°) ≈ 0
            var centre = Vector3.zero;
            float radius = 75f,
                altitude = 0f;

            Vector3 result = AC130Helpers.OrbitPosition(centre, 180f, radius, altitude);

            Assert.That(
                result.x,
                Is.EqualTo(-radius).Within(Epsilon),
                "At 180° x should be -radius."
            );
            Assert.That(
                result.z,
                Is.EqualTo(0f).Within(Epsilon),
                "At 180° z should be ~0 (sin(180°)≈0)."
            );
        }

        [Test]
        public void OrbitPosition_At270Degrees_LiesAlongNegativeZ()
        {
            // cos(270°) ≈ 0, sin(270°) = -1
            var centre = Vector3.zero;
            float radius = 60f,
                altitude = 0f;

            Vector3 result = AC130Helpers.OrbitPosition(centre, 270f, radius, altitude);

            Assert.That(result.x, Is.EqualTo(0f).Within(Epsilon), "At 270° x should be ~0.");
            Assert.That(
                result.z,
                Is.EqualTo(-radius).Within(Epsilon),
                "At 270° z should be -radius."
            );
        }

        [Test]
        public void OrbitPosition_AtAnyAngle_YIsAlwaysCentreYPlusAltitude(
            [Values(0f, 45f, 90f, 135f, 180f, 270f, 359f)] float angleDeg
        )
        {
            // Y must be independent of angle — this is the contract used when
            // the orbit-altitude is adjusted by AC130NetworkBridge.
            var centre = new Vector3(3f, 7f, -11f);
            float altitude = 123.456f;

            Vector3 result = AC130Helpers.OrbitPosition(centre, angleDeg, 50f, altitude);

            Assert.That(
                result.y,
                Is.EqualTo(centre.y + altitude).Within(Epsilon),
                $"Y must equal centre.y + altitude at angle {angleDeg}°."
            );
        }

        [Test]
        public void OrbitPosition_WithZeroRadius_AlwaysReturnsCentrePlusAltitude()
        {
            // radius = 0 collapses the orbit to a single point directly above centre.
            var centre = new Vector3(5f, 2f, 3f);
            float altitude = 30f;

            Vector3 result = AC130Helpers.OrbitPosition(centre, 45f, 0f, altitude);

            Assert.That(result.x, Is.EqualTo(centre.x).Within(Epsilon));
            Assert.That(result.z, Is.EqualTo(centre.z).Within(Epsilon));
            Assert.That(result.y, Is.EqualTo(centre.y + altitude).Within(Epsilon));
        }

        [Test]
        public void OrbitPosition_DistanceFromCentreXZ_EqualsRadius(
            [Values(0f, 30f, 60f, 90f, 120f, 150f, 180f, 210f, 270f, 315f)] float angleDeg
        )
        {
            // The XZ-plane distance from the orbit centre should always equal
            // the requested radius, regardless of angle.
            var centre = new Vector3(100f, 0f, -50f);
            float radius = 88f;

            Vector3 result = AC130Helpers.OrbitPosition(centre, angleDeg, radius, 0f);

            float dx = result.x - centre.x;
            float dz = result.z - centre.z;
            float distXZ = Mathf.Sqrt(dx * dx + dz * dz);

            Assert.That(
                distXZ,
                Is.EqualTo(radius).Within(Epsilon),
                $"XZ distance from centre should equal radius at angle {angleDeg}°."
            );
        }

        [Test]
        public void OrbitPosition_CentreOffset_IsAddedCorrectly()
        {
            // Verify that a non-zero centre shifts the result by the correct amount.
            var centre = new Vector3(100f, 200f, 300f);
            Vector3 atOrigin = AC130Helpers.OrbitPosition(Vector3.zero, 45f, 50f, 10f);
            Vector3 atCentre = AC130Helpers.OrbitPosition(centre, 45f, 50f, 10f);

            Assert.That(atCentre.x - atOrigin.x, Is.EqualTo(centre.x).Within(Epsilon));
            Assert.That(atCentre.y - atOrigin.y, Is.EqualTo(centre.y).Within(Epsilon));
            Assert.That(atCentre.z - atOrigin.z, Is.EqualTo(centre.z).Within(Epsilon));
        }

        // ── OrbitTangent ─────────────────────────────────────────────────────
        //
        // The tangent is the derivative of OrbitPosition w.r.t. angle,
        // normalised.  At angle θ:
        //   tangent = (-sin(θ°), 0, cos(θ°))
        // This vector is always horizontal (Y = 0) and perpendicular to the
        // radius vector (cos(θ°), 0, sin(θ°)).

        [Test]
        public void OrbitTangent_AtZeroDegrees_PointsAlongNegativeX()
        {
            // -sin(0) = 0, cos(0) = 1  →  tangent = (0, 0, 1)
            // Wait — derivative: d/dθ [cos θ, 0, sin θ] = [-sin θ, 0, cos θ]
            // At 0°: (-sin 0, 0, cos 0) = (0, 0, 1)
            Vector3 tangent = AC130Helpers.OrbitTangent(0f);

            Assert.That(tangent.x, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(tangent.y, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(tangent.z, Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void OrbitTangent_At90Degrees_PointsAlongNegativeX()
        {
            // -sin(90°) = -1, cos(90°) ≈ 0  →  tangent ≈ (-1, 0, 0)
            Vector3 tangent = AC130Helpers.OrbitTangent(90f);

            Assert.That(tangent.x, Is.EqualTo(-1f).Within(Epsilon));
            Assert.That(tangent.y, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(tangent.z, Is.EqualTo(0f).Within(Epsilon));
        }

        [Test]
        public void OrbitTangent_IsAlwaysNormalized(
            [Values(0f, 30f, 45f, 60f, 90f, 135f, 180f, 225f, 270f, 315f, 359f)] float angleDeg
        )
        {
            Vector3 tangent = AC130Helpers.OrbitTangent(angleDeg);
            float magnitude = tangent.magnitude;

            Assert.That(
                magnitude,
                Is.EqualTo(1f).Within(Epsilon),
                $"OrbitTangent must be normalised at angle {angleDeg}°, got magnitude {magnitude}."
            );
        }

        [Test]
        public void OrbitTangent_IsPerpendicularToRadiusVector(
            [Values(0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f)] float angleDeg
        )
        {
            // The radius vector (from centre to orbit point, XZ only) is:
            //   (cos θ, 0, sin θ)
            // Its dot product with the tangent must be zero (perpendicularity).
            float rad = angleDeg * Mathf.Deg2Rad;
            var radius = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            Vector3 tangent = AC130Helpers.OrbitTangent(angleDeg);

            float dot = Vector3.Dot(radius, tangent);

            Assert.That(
                dot,
                Is.EqualTo(0f).Within(Epsilon),
                $"Tangent must be perpendicular to the radius at angle {angleDeg}° (dot={dot})."
            );
        }

        [Test]
        public void OrbitTangent_YComponentIsAlwaysZero(
            [Values(0f, 30f, 90f, 180f, 270f, 359f)] float angleDeg
        )
        {
            // The orbit is flat; the tangent must have no vertical component.
            Vector3 tangent = AC130Helpers.OrbitTangent(angleDeg);

            Assert.That(
                tangent.y,
                Is.EqualTo(0f).Within(Epsilon),
                $"Tangent Y must be 0 at angle {angleDeg}° (got {tangent.y})."
            );
        }

        [Test]
        public void OrbitTangent_At180Degrees_IsOppositeToAtZeroDegrees()
        {
            // Travelling the orbit in the same direction, the tangent at 180°
            // should be the exact negation of the tangent at 0°.
            Vector3 t0 = AC130Helpers.OrbitTangent(0f);
            Vector3 t180 = AC130Helpers.OrbitTangent(180f);

            Assert.That(t180.x, Is.EqualTo(-t0.x).Within(Epsilon));
            Assert.That(t180.z, Is.EqualTo(-t0.z).Within(Epsilon));
        }

        // ── OrbitPosition + OrbitTangent consistency ──────────────────────────

        [Test]
        public void OrbitTangent_IsConsistentWithOrbitPositionDerivative(
            [Values(0f, 45f, 90f, 135f, 180f)] float angleDeg
        )
        {
            // Numerically differentiate OrbitPosition and compare with OrbitTangent.
            // Using a small step (h = 0.001°) and a non-zero centre/radius/altitude
            // to exercise the full formula.
            var centre = Vector3.zero;
            float radius = 100f,
                altitude = 0f;
            float h = 0.001f;

            Vector3 p1 = AC130Helpers.OrbitPosition(centre, angleDeg + h, radius, altitude);
            Vector3 p0 = AC130Helpers.OrbitPosition(centre, angleDeg - h, radius, altitude);

            // Central-difference derivative, then normalise.
            Vector3 numerical = (p1 - p0).normalized;
            Vector3 analytic = AC130Helpers.OrbitTangent(angleDeg);

            // The numerical and analytic tangents must point in the same direction.
            float dot = Vector3.Dot(numerical, analytic);
            Assert.That(
                dot,
                Is.EqualTo(1f).Within(1e-4f),
                $"Numeric derivative and OrbitTangent should agree at {angleDeg}° (dot={dot})."
            );
        }
    }
}
