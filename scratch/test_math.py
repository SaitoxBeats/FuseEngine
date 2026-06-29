import numpy as np

# Simulate C# Matrix4x4 behavior
# Vector4.Transform(v, matrix) is v * matrix (row vector)
# shearMat.M31 = k means Row 3, Col 1 is k.
# Identity is 4x4.
k = 2.0
shearMat = np.identity(4)
shearMat[2, 0] = k # M31 (0-indexed: row 2, col 0)

inverted = np.linalg.inv(shearMat)
invTranspose = np.transpose(inverted)

vRight = np.array([1, 0, 0, -1])
vFront = np.array([0, 0, 1, -1])

rightNew = np.dot(vRight, invTranspose)
frontNew = np.dot(vFront, invTranspose)

print(f"Right original: {vRight}")
print(f"Right new: {rightNew}")
print(f"Front original: {vFront}")
print(f"Front new: {frontNew}")
