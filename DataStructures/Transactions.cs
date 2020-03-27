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
      string description)
    {
      Currency = currency;
      Id = id;
      State = state;
      Type = type;
      CreationTime = TimeZoneInfo.ConvertTime(creationTime ?? DateTime.Now, TimeZoneInfo.Local);
      IsConfirmed = isConfirmed;
      Amount = amount;
      Description = description;
    }

    public Currency Currency { get; set; }
    public string Id { get; set; }
    public BlockchainTransactionState State { get; set; }
    public BlockchainTransactionType Type { get; set; }
    public DateTime? CreationTime { get; set; }
    public bool IsConfirmed { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; }
    public BlockInfo BlockInfo => throw new NotImplementedException();
  }
}