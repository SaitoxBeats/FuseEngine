using System;
using System.Numerics;

class Program
{
    static void Main()
    {
        var shearMat = Matrix4x4.Identity;
        shearMat.M31 = 2.0f; // k = 2
        
        Matrix4x4.Invert(shearMat, out var inverted);
        var invTranspose = Matrix4x4.Transpose(inverted);
        
        var vRight = new Vector4(1, 0, 0, -1);
        var vFront = new Vector4(0, 0, 1, -1);
        
        var rightNew = Vector4.Transform(vRight, invTranspose);
        var frontNew = Vector4.Transform(vFront, invTranspose);
        
        Console.WriteLine($"Right original: {vRight}");
        Console.WriteLine($"Right new: {rightNew}");
        Console.WriteLine($"Front original: {vFront}");
        Console.WriteLine($"Front new: {frontNew}");
    }
}
