using System;
using System.IO;

namespace atomex_frontend.atomex_data_structures
{
  public enum AvailableCurrencies
  {
    Bitcoin,
    Ethereum,
    Litecoin,
    Tezos
  }

  public enum AvailableCurrenciesAbbr
  {
    BTC,
    ETH,
    LTC,
    XTZ
  }

  public class Helper
  {
    public static AvailableCurrenciesAbbr GetAbbr(AvailableCurrencies currency)
    {
      return (AvailableCurrenciesAbbr)currency;
    }

    public static Stream GenerateStreamFromString(string s)
    {
      var stream = new MemoryStream();
      var writer = new StreamWriter(stream);
      writer.Write(s);
      writer.Flush();
      stream.Position = 0;
      return stream;
    }
  }

  public class CurrencyData
  {
    public CurrencyData(AvailableCurrenciesAbbr currencyName, double balance, double dollarValue, double percent)
    {
      CurrencyName = currencyName;
      Balance = balance;
      DollarValue = dollarValue;
      Percent = percent;
    }

    public AvailableCurrenciesAbbr CurrencyName;
    public double Balance;

    public double DollarValue;

    public double Percent;
  }

}