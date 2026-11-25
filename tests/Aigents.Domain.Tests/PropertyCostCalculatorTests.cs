using Aigents.Domain.Finance;

namespace Aigents.Domain.Tests;

public class PropertyCostCalculatorTests
{
    [Fact]
    public void QldFirstHomeBuyerUnder700k_PaysZeroStampDuty()
    {
        var est = PropertyCostCalculator.Calculate(680_000m, "QLD", BuyerOccupancyType.FirstHomeBuyer, depositPercent: 20);
        Assert.Equal("QLD", est.State);
        Assert.Equal(0m, est.EstimatedStampDuty);
        Assert.Equal(136_000m, est.DepositAmount);
        Assert.Equal(544_000m, est.LoanAmount);
        Assert.Equal(136_000m, est.TotalUpfrontRequired);
        Assert.True(est.EstimatedMonthlyRepayment > 3_000m && est.EstimatedMonthlyRepayment < 3_600m);
        Assert.Contains("Illustrative estimate", est.Disclaimer);
    }

    [Fact]
    public void QldOwnerOccupier_Under350kPaysOnePercentConcession_AndSaves7175Above350k()
    {
        var at100k = PropertyCostCalculator.EstimateStampDuty(100_000m, "QLD", BuyerOccupancyType.OwnerOccupier);
        Assert.Equal(1_000m, at100k);

        var owner = PropertyCostCalculator.Calculate(1_200_000m, "Queensland", BuyerOccupancyType.OwnerOccupier);
        var investor = PropertyCostCalculator.Calculate(1_200_000m, "QLD", BuyerOccupancyType.Investor);

        Assert.Equal("QLD", owner.State);
        Assert.Equal(7_175m, investor.EstimatedStampDuty - owner.EstimatedStampDuty);
    }

    [Fact]
    public void TasAndActBrackets_AreStrictlyMonotonicAcrossBoundaries()
    {
        var tasAt200k = PropertyCostCalculator.EstimateStampDuty(200_000m, "TAS", BuyerOccupancyType.OwnerOccupier);
        var tasJustAbove = PropertyCostCalculator.EstimateStampDuty(200_001m, "TAS", BuyerOccupancyType.OwnerOccupier);
        Assert.True(tasJustAbove >= tasAt200k, $"TAS duty dropped at $200k boundary: {tasAt200k} -> {tasJustAbove}");

        var actAtThreshold = PropertyCostCalculator.EstimateStampDuty(1_455_000m, "ACT", BuyerOccupancyType.OwnerOccupier);
        var actJustAbove = PropertyCostCalculator.EstimateStampDuty(1_455_001m, "ACT", BuyerOccupancyType.OwnerOccupier);
        Assert.True(actJustAbove >= actAtThreshold && actJustAbove - actAtThreshold <= 2m,
            $"ACT duty had cliff at $1.455M boundary: {actAtThreshold} -> {actJustAbove}");
    }

    [Fact]
    public void EstimateStampDuty_NormalizesStateNamesDirectly()
    {
        var direct = PropertyCostCalculator.EstimateStampDuty(1_000_000m, "nsw", BuyerOccupancyType.Investor);
        var upper = PropertyCostCalculator.EstimateStampDuty(1_000_000m, "NSW", BuyerOccupancyType.Investor);
        Assert.Equal(upper, direct);
    }

    [Theory]
    [InlineData("NSW")]
    [InlineData("VIC")]
    [InlineData("WA")]
    [InlineData("SA")]
    [InlineData("TAS")]
    [InlineData("ACT")]
    [InlineData("NT")]
    public void AllAustralianStates_ReturnPositiveDutyFor1Point5M(string state)
    {
        var est = PropertyCostCalculator.Calculate(1_500_000m, state, BuyerOccupancyType.OwnerOccupier);
        Assert.Equal(state, est.State);
        Assert.True(est.EstimatedStampDuty > 20_000m);
        Assert.Equal(est.DepositAmount + est.EstimatedStampDuty, est.TotalUpfrontRequired);
    }

    [Fact]
    public void ExtremeAndNegativeInputs_NeverOverflowOrReturnNaN()
    {
        var neg = PropertyCostCalculator.Calculate(-50_000m, null, BuyerOccupancyType.OwnerOccupier, depositPercent: 200);
        Assert.Equal(0m, neg.PurchasePrice);
        Assert.Equal(0m, neg.EstimatedStampDuty);
        Assert.Equal(0m, neg.EstimatedMonthlyRepayment);
        Assert.Equal("QLD", neg.State);

        var huge = PropertyCostCalculator.Calculate(decimal.MaxValue, "NT", BuyerOccupancyType.Investor);
        Assert.Equal(PropertyCostCalculator.MaxSupportedPrice, huge.PurchasePrice);
        Assert.True(huge.EstimatedStampDuty > 0m);

        var tinyRate = PropertyCostCalculator.EstimateMonthlyRepayment(500_000m, 0.00000000001m, 30);
        Assert.True(tinyRate > 0m);

        var hugeTerm = PropertyCostCalculator.EstimateMonthlyRepayment(500_000m, 45m, int.MaxValue);
        Assert.True(hugeTerm > 0m);
    }
}
