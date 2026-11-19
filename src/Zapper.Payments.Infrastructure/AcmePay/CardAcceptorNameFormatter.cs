namespace Zapper.Payments.Infrastructure.AcmePay;

public static class CardAcceptorNameFormatter
{
    public static string Format(
        string tradingName,
        string city,
        string region,
        string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tradingName);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        var merchantName = $"ZAP*{tradingName.Trim().ToUpperInvariant()}";
        var merchant = merchantName.Length > 23
            ? merchantName[..23]
            : merchantName.PadRight(23);

        var merchantCity = city.Trim().ToUpperInvariant();
        var formattedCity = merchantCity.Length > 13
            ? merchantCity[..13]
            : merchantCity.PadRight(13);

        var formattedRegion = region.Trim().ToUpperInvariant()[..2];
        var formattedCountry = countryCode.Trim().ToUpperInvariant()[..2];

        var result = merchant
                     + formattedCity
                     + formattedRegion
                     + formattedCountry;

        if (result.Length != 40)
        {
            throw new InvalidOperationException(
                "CardAcceptorName must be exactly 40 characters.");
        }

        return result;
    }
}