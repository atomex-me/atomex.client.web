using System;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using Atomex;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.BitcoinBased;
using Atomex.EthereumTokens;
using atomex_frontend.atomex_data_structures;

using Atomex.TezosTokens;

namespace atomex_frontend.Common
{
  public static class Helper
  {
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
      decimal number;
      return decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    public static string DecimalToStr(decimal value)
    {
      return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string IntToStr(int value) {
      return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string DecimalToStr(decimal value, string format)
    {
      return value.ToString(format, CultureInfo.InvariantCulture);
    }

    public static void RunAsync(Task task)
    {
      task.ContinueWith(t =>
      {
        Console.WriteLine("Unexpected Error", t.Exception);

      }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public static decimal TruncateByFormat(decimal d, string format)
    {
      var s = d.ToString(format, CultureInfo.InvariantCulture);

      return decimal.Parse(s, CultureInfo.InvariantCulture);
    }
  }

  public static class CurrHelper
  {
    public static decimal GetTransAmount(EthereumTransaction tx)
    {
      if (tx.Currency is Tether || tx.Currency is TBTC || tx.Currency is WBTC)
      {
        var Erc20 = tx.Currency as ERC20;
        var usdtResult = 0m;

        if (tx.Type.HasFlag(BlockchainTransactionType.SwapRedeem) ||
            tx.Type.HasFlag(BlockchainTransactionType.SwapRefund))
          usdtResult += Erc20.TokenDigitsToTokens(tx.Amount);
        else
        {
          if (tx.Type.HasFlag(BlockchainTransactionType.Input))
            usdtResult += Erc20.TokenDigitsToTokens(tx.Amount);
          if (tx.Type.HasFlag(BlockchainTransactionType.Output))
            usdtResult += -Erc20.TokenDigitsToTokens(tx.Amount);
        }

        tx.InternalTxs?.ForEach(t => usdtResult += GetTransAmount(t));
        return usdtResult;
      }

      var result = 0m;

      if (tx.Type.HasFlag(BlockchainTransactionType.Input))
        result += Ethereum.WeiToEth(tx.Amount);

      if (tx.Type.HasFlag(BlockchainTransactionType.Output))
        result += -Ethereum.WeiToEth(tx.Amount + tx.GasUsed * tx.GasPrice);

      tx.InternalTxs?.ForEach(t => result += GetTransAmount(t));

      return result;
    }

    public static decimal GetFee(EthereumTransaction tx)
    {
      var result = 0m;

      if (tx.Type.HasFlag(BlockchainTransactionType.Output))
        result += Ethereum.WeiToEth(tx.GasUsed * tx.GasPrice);

      tx.InternalTxs?.ForEach(t => result += GetFee(t));

      return result;
    }

    public static decimal GetTransAmount(TezosTransaction tx)
    {
      if (tx.Currency is FA12)
      {
        var fa12Result = 0m;

        if (tx.Type.HasFlag(BlockchainTransactionType.SwapRedeem) ||
            tx.Type.HasFlag(BlockchainTransactionType.SwapRefund))
          fa12Result += tx.Amount.FromTokenDigits(tx.Currency.DigitsMultiplier);
        else
        {
          if (tx.Type.HasFlag(BlockchainTransactionType.Input))
            fa12Result += tx.Amount.FromTokenDigits(tx.Currency.DigitsMultiplier);
          if (tx.Type.HasFlag(BlockchainTransactionType.Output))
            fa12Result += -tx.Amount.FromTokenDigits(tx.Currency.DigitsMultiplier);
        }

        tx.InternalTxs?.ForEach(t => fa12Result += GetTransAmount(t));

        return fa12Result;
      }

      var result = 0m;

      if (tx.Type.HasFlag(BlockchainTransactionType.Input))
        result += tx.Amount / tx.Currency.DigitsMultiplier;

      var includeFee = tx.Currency.Name == tx.Currency.FeeCurrencyName;
      var fee = includeFee ? tx.Fee : 0;

      if (tx.Type.HasFlag(BlockchainTransactionType.Output))
        result += -(tx.Amount + fee) / tx.Currency.DigitsMultiplier;

      tx.InternalTxs?.ForEach(t => result += GetTransAmount(t));

      return result;
    }

    public static decimal GetFee(TezosTransaction tx)
    {
      var result = 0m;

      if (tx.Type.HasFlag(BlockchainTransactionType.Output))
        result += Tezos.MtzToTz(tx.Fee);

      tx.InternalTxs?.ForEach(t => result += GetFee(t));

      return result;
    }

    public static decimal GetTransAmount(IBitcoinBasedTransaction tx)
    {
      return tx.Amount / (decimal)tx.Currency.DigitsMultiplier;
    }

    public static decimal GetFee(IBitcoinBasedTransaction tx)
    {
      return tx.Fees != null
          ? tx.Type.HasFlag(BlockchainTransactionType.Output)
              ? tx.Fees.Value / (decimal)tx.Currency.DigitsMultiplier
              : 0
          : 0;
    }

    public static string GetTransDescription(IBlockchainTransaction tx, decimal amount, decimal fee)
    {
      var netAmount = amount + fee;
      string Description = "Unknown transaction";
      if (tx.Type.HasFlag(BlockchainTransactionType.SwapPayment))
      {
        Description = $"Swap payment {Math.Abs(netAmount).ToString("0." + new String('#', tx.Currency.Digits))} {tx.Currency.Name}";
        return Description;
      }
      else if (tx.Type.HasFlag(BlockchainTransactionType.SwapRefund))
      {
        Description = $"Swap refund {Math.Abs(netAmount).ToString("0." + new String('#', tx.Currency.Digits))} {tx.Currency.Name}";
        return Description;
      }
      else if (tx.Type.HasFlag(BlockchainTransactionType.SwapRedeem))
      {
        Description = $"Swap redeem {Math.Abs(netAmount).ToString("0." + new String('#', tx.Currency.Digits))} {tx.Currency.Name}";
        return Description;
      }
      else if (tx.Type.HasFlag(BlockchainTransactionType.TokenApprove))
      {
        Description = $"Token approve";
        return Description;
      }
      else if (tx.Type.HasFlag(BlockchainTransactionType.TokenCall))
      {
        Description = $"Token call";
      }
      else if (tx.Type.HasFlag(BlockchainTransactionType.SwapCall))
      {
        Description = $"Token swap call";
      }
      else if (amount <= 0) //tx.Type.HasFlag(BlockchainTransactionType.Output))
      {
        Description = $"Sent {Math.Abs(netAmount).ToString("0." + new String('#', tx.Currency.Digits))} {tx.Currency.Name}";
        return Description;
      }
      else if (amount > 0) //tx.Type.HasFlag(BlockchainTransactionType.Input)) // has outputs
      {
        Description = $"Received {Math.Abs(netAmount).ToString("0." + new String('#', tx.Currency.Digits))} {tx.Currency.Name}";
        return Description;
      }

      return Description;
    }

    public static string GetTxDirection(Transaction tx)
    {
      if (tx.Amount <= 0)
      {
        return "To";
      }

      if (tx.Amount > 0)
      {
        return "From";
      }

      return String.Empty;
    }

    public static string GetTxAlias(Transaction tx)
    {
      if (!String.IsNullOrEmpty(tx.Alias))
      {
        return tx.Alias;
      }
      else
      {
        if (tx.Amount <= 0)
        {
          return tx.To;
        }

        if (tx.Amount > 0)
        {
          return tx.From;
        }
      }

      return String.Empty;
    }
  }
}