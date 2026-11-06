# Shared mathematics

Namespace: `DE.Share.Foundation.Mathematics`. Source: `Client/Demo/Assets/DEFramework/Scripts/DE.Share.Foundation/Mathematics`.
The same pure C# sources compile into the Unity and server foundation assemblies.

## Conventions

- Components use float; normalization and inversion use double intermediates.
- Types are immutable value types. Equality and hash codes use exact component values; use Approximately for tolerance-based comparisons.
- Division and homogeneous-coordinate guards reject exact positive/negative zero; small nonzero values remain valid.
- Angles are radians. Rotations use the right-hand rule. Quaternion components are X, Y, Z, W.
- Matrices expose row-major M11... entries and a zero-based [row, column] indexer. They multiply column vectors.
- A * B applies B first for matrices and quaternions. TRS is Translation * Rotation * Scale.
- Matrix4x4.TransformPoint uses homogeneous W division; TransformVector ignores translation.
- Operations assume finite inputs and do not validate NaN, infinity or overflow results.
- Vector and quaternion Normalize reject zero inputs. TryNormalize returns false and the zero vector or identity quaternion.
- Rotation operations normalize their quaternion input.
- Inverse rejects singular input; TryInverse returns false with Zero for matrices and Identity for quaternions.
  Matrices also reject numerically rank-deficient input through scaled pivot checks and check both inverse-product residuals after float conversion.
- Lerp and Slerp clamp the interpolation parameter to [0, 1]. Quaternion interpolation follows the shortest rotation path.

## API scope

- MathUtil: constants, clamp and relative/absolute approximate comparison.
- Vector2/3/4: arithmetic, scalar/component multiplication, length, normalization, dot product, distance, interpolation and min/max.
- Vector3 additionally provides cross product and reflection.
- Quaternion: Hamilton product, vector rotation, conjugate/inverse, axis-angle and yaw/pitch/roll construction, matrix conversion and Lerp/Slerp.
  Yaw/pitch/roll uses Y * X * Z composition (roll is applied first).
- Matrix3x3/4x4: arithmetic, matrix/vector multiplication, transpose, determinant, inverse, translation/scale/rotation construction.
  Matrix3x3 supports homogeneous 2D point transforms and linear 3D transforms; Matrix4x4 provides 3D TRS.

## Implementation and verification

- [x] Implement the foundation math types and document their conventions.
- [x] Include Unity metadata and keep the foundation assembly independent of Unity engine references.
- [x] Build both solutions, run existing tests, and perform independent mathematical code review.
- Do not add test cases, per the user's standing instruction.

Verification: framework and client builds passed with zero warnings/errors; all 22 existing tests passed.
Independent formula review completed, including the scaled-pivot and inverse-residual checks. No math-specific test cases were added.
