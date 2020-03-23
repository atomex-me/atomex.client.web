using System;
using System.IO;

namespace atomex_frontend.Common
{
  public static class Helper
  {
    // public static AvailableCurrenciesAbbr GetAbbr(AvailableCurrencies currency)
    // {
    //   return (AvailableCurrenciesAbbr)currency;
    // }

    public static Stream GenerateStreamFromString(string s)
    {
      var stream = new MemoryStream();
      var writer = new StreamWriter(stream);
      writer.Write(s);
      writer.Flush();
      stream.Position = 0;
      return stream;
    }

    public static decimal SetPrecision(decimal value, int precision)
    {
      return decimal.Round(value, precision, MidpointRounding.AwayFromZero);
    }

    public static decimal StrToDecimal(string str)
    {
      string res = str.Replace(".", ",");
      decimal number;
      return decimal.TryParse(res, out number) ? number : 0;
    }

    public static string DecimalToStr(decimal value)
    {
      return value.ToString().Replace(",", ".");
    }
  }
}