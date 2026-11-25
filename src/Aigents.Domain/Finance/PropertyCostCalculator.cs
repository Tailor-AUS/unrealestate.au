namespace Aigents.Domain.Finance;

public enum BuyerOccupancyType
{
    OwnerOccupier,
    FirstHomeBuyer,
    Investor
}

public sealed record PropertyCostEstimate(
    decimal PurchasePrice,
    string State,
    BuyerOccupancyType BuyerType,
    decimal DepositAmount,
    decimal LoanAmount,
    decimal EstimatedStampDuty,
    decimal EstimatedMonthlyRepayment,
    decimal TotalUpfrontRequired,
    decimal AnnualInterestRatePercent,
    int LoanTermYears,
    string Disclaimer);

/// <summary>
/// Illustrative Australian residential transfer (stamp) duty and mortgage repayment calculator.
/// Pure domain logic — zero external network calls, honest illustrative estimates only.
/// </summary>
public static class PropertyCostCalculator
{
    public const decimal DefaultInterestRatePercent = 6.10m;
    public const int DefaultLoanTermYears = 30;
    public const decimal MaxSupportedPrice = 1_000_000_000m;

    public const string IllustrativeDisclaimer =
        "Illustrative estimate only (standard state transfer duty brackets & P&I repayment at stated rate). " +
        "Confirm exact duty, concessions, LMI, and conveyancing fees with your state revenue office or conveyancer.";

    public static PropertyCostEstimate Calculate(
        decimal purchasePrice,
        string? state,
        BuyerOccupancyType buyerType = BuyerOccupancyType.OwnerOccupier,
        int depositPercent = 20,
        decimal annualInterestRatePercent = DefaultInterestRatePercent,
        int loanTermYears = DefaultLoanTermYears)
    {
        var price = Math.Clamp(purchasePrice, 0m, MaxSupportedPrice);
        var normalizedState = NormalizeState(state);
        var clampedDepositPct = Math.Clamp(depositPercent, 5, 90);
        var clampedTerm = Math.Clamp(loanTermYears, 5, 40);
        var clampedRate = Math.Clamp(annualInterestRatePercent, 0.5m, 18.0m);

        var deposit = Math.Round(price * (clampedDepositPct / 100m), 0, MidpointRounding.AwayFromZero);
        var loan = Math.Max(0m, price - deposit);
        var duty = EstimateStampDuty(price, normalizedState, buyerType);
        var monthly = EstimateMonthlyRepayment(loan, clampedRate, clampedTerm);
        var upfront = deposit + duty;

        return new PropertyCostEstimate(
            PurchasePrice: price,
            State: normalizedState,
            BuyerType: buyerType,
            DepositAmount: deposit,
            LoanAmount: loan,
            EstimatedStampDuty: duty,
            EstimatedMonthlyRepayment: monthly,
            TotalUpfrontRequired: upfront,
            AnnualInterestRatePercent: clampedRate,
            LoanTermYears: clampedTerm,
            Disclaimer: IllustrativeDisclaimer);
    }

    public static string NormalizeState(string? state)
    {
        var s = (state ?? string.Empty).Trim().ToUpperInvariant();
        return s switch
        {
            "QLD" or "QUEENSLAND" => "QLD",
            "NSW" or "NEW SOUTH WALES" => "NSW",
            "VIC" or "VICTORIA" => "VIC",
            "WA" or "WESTERN AUSTRALIA" => "WA",
            "SA" or "SOUTH AUSTRALIA" => "SA",
            "TAS" or "TASMANIA" => "TAS",
            "ACT" or "AUSTRALIAN CAPITAL TERRITORY" => "ACT",
            "NT" or "NORTHERN TERRITORY" => "NT",
            _ => "QLD"
        };
    }

    public static decimal EstimateMonthlyRepayment(decimal loanAmount, decimal annualRatePercent, int years)
    {
        var safeLoan = Math.Clamp(loanAmount, 0m, MaxSupportedPrice);
        var safeYears = Math.Clamp(years, 0, 50);
        if (safeLoan <= 0m || safeYears <= 0) return 0m;

        var months = safeYears * 12;
        var safeRate = Math.Clamp(annualRatePercent, 0m, 50m);
        var monthlyRate = (double)(safeRate / 100m) / 12.0;

        if (monthlyRate <= 1e-9)
            return Math.Round(safeLoan / months, 0, MidpointRounding.AwayFromZero);

        var factor = Math.Pow(1.0 + monthlyRate, months);
        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 1.0)
            return Math.Round(safeLoan * (safeRate / 100m) / 12m, 0, MidpointRounding.AwayFromZero);

        var payment = (double)safeLoan * (monthlyRate * factor) / (factor - 1.0);
        if (double.IsNaN(payment) || double.IsInfinity(payment) || payment > (double)MaxSupportedPrice)
            return Math.Round(safeLoan, 0, MidpointRounding.AwayFromZero);

        return Math.Round((decimal)payment, 0, MidpointRounding.AwayFromZero);
    }

    public static decimal EstimateStampDuty(decimal price, string? state, BuyerOccupancyType buyerType)
    {
        var safePrice = Math.Clamp(price, 0m, MaxSupportedPrice);
        if (safePrice <= 0m) return 0m;

        var normalized = NormalizeState(state);
        return normalized switch
        {
            "QLD" => EstimateQldDuty(safePrice, buyerType),
            "NSW" => EstimateNswDuty(safePrice, buyerType),
            "VIC" => EstimateVicDuty(safePrice, buyerType),
            "WA" => EstimateWaDuty(safePrice, buyerType),
            "SA" => EstimateSaDuty(safePrice),
            "TAS" => EstimateTasDuty(safePrice, buyerType),
            "ACT" => EstimateActDuty(safePrice, buyerType),
            "NT" => EstimateNtDuty(safePrice),
            _ => EstimateQldDuty(safePrice, buyerType)
        };
    }

    private static decimal EstimateQldDuty(decimal p, BuyerOccupancyType type)
    {
        // Standard QLD transfer duty brackets
        decimal baseDuty = p switch
        {
            <= 5_000m => 0m,
            <= 75_000m => (p - 5_000m) * 0.015m,
            <= 540_000m => 1_050m + (p - 75_000m) * 0.035m,
            <= 1_000_000m => 17_325m + (p - 540_000m) * 0.045m,
            _ => 38_025m + (p - 1_000_000m) * 0.0575m
        };

        if (type == BuyerOccupancyType.Investor)
            return RoundDollar(baseDuty);

        // QLD Home Concession: 1% on the first $350,000 ($3,500 vs $10,675 = $7,175 saving above $350k)
        decimal homeConcessionDuty = p <= 350_000m
            ? p * 0.01m
            : Math.Max(0m, baseDuty - 7_175m);

        if (type == BuyerOccupancyType.OwnerOccupier)
            return RoundDollar(homeConcessionDuty);

        // First home concession: $0 up to $700k, sliding scale to $800k
        if (p <= 700_000m) return 0m;
        if (p < 800_000m)
            return RoundDollar(homeConcessionDuty * ((p - 700_000m) / 100_000m));
        return RoundDollar(homeConcessionDuty);
    }

    private static decimal EstimateNswDuty(decimal p, BuyerOccupancyType type)
    {
        decimal baseDuty = p switch
        {
            <= 17_000m => p * 0.0125m,
            <= 36_000m => 212.50m + (p - 17_000m) * 0.015m,
            <= 97_000m => 497.50m + (p - 36_000m) * 0.0175m,
            <= 364_000m => 1_565m + (p - 97_000m) * 0.035m,
            <= 1_212_000m => 10_910m + (p - 364_000m) * 0.045m,
            <= 3_636_000m => 49_070m + (p - 1_212_000m) * 0.055m,
            _ => 182_390m + (p - 3_636_000m) * 0.07m
        };

        if (type == BuyerOccupancyType.FirstHomeBuyer)
        {
            if (p <= 800_000m) return 0m;
            if (p < 1_000_000m) return RoundDollar(baseDuty * ((p - 800_000m) / 200_000m));
        }

        return RoundDollar(baseDuty);
    }

    private static decimal EstimateVicDuty(decimal p, BuyerOccupancyType type)
    {
        decimal baseDuty = p switch
        {
            <= 25_000m => p * 0.014m,
            <= 130_000m => 350m + (p - 25_000m) * 0.024m,
            <= 960_000m => 2_870m + (p - 130_000m) * 0.06m,
            <= 2_000_000m => p * 0.055m,
            _ => 110_000m + (p - 2_000_000m) * 0.065m
        };

        if (type == BuyerOccupancyType.FirstHomeBuyer)
        {
            if (p <= 600_000m) return 0m;
            if (p < 750_000m) return RoundDollar(baseDuty * ((p - 600_000m) / 150_000m));
        }

        return RoundDollar(baseDuty);
    }

    private static decimal EstimateWaDuty(decimal p, BuyerOccupancyType type)
    {
        decimal baseDuty = p switch
        {
            <= 120_000m => p * 0.019m,
            <= 150_000m => 2_280m + (p - 120_000m) * 0.0285m,
            <= 360_000m => 3_135m + (p - 150_000m) * 0.038m,
            <= 725_000m => 11_115m + (p - 360_000m) * 0.0475m,
            _ => 28_452.50m + (p - 725_000m) * 0.0515m
        };

        if (type == BuyerOccupancyType.FirstHomeBuyer)
        {
            if (p <= 450_000m) return 0m;
            if (p < 600_000m) return RoundDollar(baseDuty * ((p - 450_000m) / 150_000m));
        }

        return RoundDollar(baseDuty);
    }

    private static decimal EstimateSaDuty(decimal p)
    {
        decimal baseDuty = p switch
        {
            <= 100_000m => p * 0.0283m,
            <= 200_000m => 2_830m + (p - 100_000m) * 0.04m,
            <= 300_000m => 6_830m + (p - 200_000m) * 0.045m,
            <= 500_000m => 11_330m + (p - 300_000m) * 0.05m,
            _ => 21_330m + (p - 500_000m) * 0.055m
        };
        return RoundDollar(baseDuty);
    }

    private static decimal EstimateTasDuty(decimal p, BuyerOccupancyType type)
    {
        // Continuous, monotonic marginal brackets
        decimal baseDuty = p switch
        {
            <= 200_000m => p * 0.03m,
            <= 375_000m => 6_000m + (p - 200_000m) * 0.04m,
            <= 725_000m => 13_000m + (p - 375_000m) * 0.0425m,
            _ => 27_875m + (p - 725_000m) * 0.045m
        };

        if (type == BuyerOccupancyType.FirstHomeBuyer && p <= 750_000m)
            return 0m;

        return RoundDollar(baseDuty);
    }

    private static decimal EstimateActDuty(decimal p, BuyerOccupancyType type)
    {
        // Continuous, monotonic marginal brackets (no discontinuity at $1,455,000)
        decimal baseDuty = p switch
        {
            <= 260_000m => p * 0.0049m,
            <= 500_000m => 1_274m + (p - 260_000m) * 0.028m,
            <= 750_000m => 7_994m + (p - 500_000m) * 0.0432m,
            <= 1_000_000m => 18_794m + (p - 750_000m) * 0.059m,
            <= 1_455_000m => 33_544m + (p - 1_000_000m) * 0.064m,
            _ => 62_664m + (p - 1_455_000m) * 0.0454m
        };

        if (type == BuyerOccupancyType.FirstHomeBuyer && p <= 1_000_000m)
            return 0m;

        return RoundDollar(baseDuty);
    }

    private static decimal EstimateNtDuty(decimal p)
    {
        decimal baseDuty = p <= 525_000m
            ? (0.06571441m * (p / 1000m) * (p / 1000m)) + (15m * (p / 1000m))
            : p * 0.0495m;
        return RoundDollar(baseDuty);
    }

    private static decimal RoundDollar(decimal amount) =>
        Math.Max(0m, Math.Round(amount, 0, MidpointRounding.AwayFromZero));
}
