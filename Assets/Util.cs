using System;

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