using System;
using UnityEngine;

static class Util {
    public static int Mod(int dividend, int divisor) {
        int remainder = dividend % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }

    public static int DivDown(int dividend, int divisor) {
        return (dividend >= 0 ? dividend : dividend - divisor + 1) / divisor;
    }

    public static int DivDown(float dividend, float divisor) {
        return (int)Math.Floor(dividend / divisor);
    }
}

namespace Extensions {
    public static class Vector3Extensions {
        public static Vector3 Floor(this Vector3 vector) {
            return new Vector3(Mathf.Floor(vector.x), Mathf.Floor(vector.y), Mathf.Floor(vector.z));
        }
        public static Vector3 Repeat(this Vector3 vector, Vector3 length) {
            return new Vector3(Mathf.Repeat(vector.x, length.x), Mathf.Repeat(vector.y, length.y), Mathf.Repeat(vector.z, length.z));
        }
    }
}