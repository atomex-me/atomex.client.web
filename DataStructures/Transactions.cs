using System;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Core;

namespace atomex_frontend.atomex_data_structures
{
  public class Transaction : IBlockchainTransaction
  {
    public Transaction(
      Currency currency,
      string id,
      BlockchainTransactionState state,
      BlockchainTransactionType type,
      DateTime? creationTime,
      bool isConfirmed,
      decimal amount,
      string description,
      decimal fee = 0,
      string from = null,
      string to = null,
      decimal gasPrice = 0,
      decimal gasLimit = 0,
      decimal gasUsed = 0,
      bool isInternal = false,
      string alias = null
    )
    {

      if (creationTime != null && creationTime.Value.Year == 1970)
      {
        creationTime = DateTime.Now;
      }
      Currency = currency;
      Id = id;
      State = state;
      Type = type;
      CreationTime = TimeZoneInfo.ConvertTime(creationTime ?? DateTime.Now, TimeZoneInfo.Local);
      IsConfirmed = isConfirmed;
      Amount = amount;
      Description = description;
      Fee = fee;
      From = from;
      To = to;
      GasPrice = gasPrice;
      GasLimit = gasLimit;
      GasUsed = gasUsed;
      IsInternal = isInternal;
      Alias = alias;
    }

    public Currency Currency { get; set; }
    public string Id { get; set; }
    public BlockchainTransactionState State { get; set; }
    public BlockchainTransactionType Type { get; set; }
    public DateTime? CreationTime { get; set; }
    public bool IsConfirmed { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; }
    public decimal Fee { get; set; }

    public string From { get; set; }

    public string To { get; set; }

    public decimal GasPrice { get; set; }

    public decimal GasLimit { get; set; }

    public decimal GasUsed { get; set; }

    public bool IsInternal { get; set; }

    public string Alias { get; set; }

    public BlockInfo BlockInfo => throw new NotImplementedException();
  }
}