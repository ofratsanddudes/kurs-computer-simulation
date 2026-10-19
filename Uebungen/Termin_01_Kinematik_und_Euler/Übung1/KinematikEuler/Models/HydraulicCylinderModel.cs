namespace KinematikEuler.Models;

public class HydraulicCylinderModel
{
    public const double Mass = 80.0;
    public const double F_Vor = 60_000.0;
    public const double X_D = 0.65;
    public const double X_Anschlag = 0.80;

    public const double D_Lin = 250.0;
    public const double C_Blend = 15_000.0;
    public const double C_Kontakt = 5e7;
    public const double D_Kontakt = 40_000.0;

    public double ComputeDampingForce(double x, double v)
    {
        double fLin = D_Lin * v;
        return (x < X_D) ? fLin : fLin + C_Blend * v * Math.Abs(v);
    }

    public double ComputeEndStopForce(double x, double v)
    {
        if (x < X_Anschlag)
            return 0.0;

        return C_Kontakt * (x - X_Anschlag) + D_Kontakt * v;
    }

    public (double A, double FDaempf) Evaluate(double x, double v)
    {
        double fVor = (x < X_Anschlag) ? F_Vor : 0.0;
        double fDaempf = ComputeDampingForce(x, v);
        double fAnschlag = ComputeEndStopForce(x, v);

        double fNet = fVor - fDaempf - fAnschlag;
        return (fNet / Mass, fDaempf);
    }
}