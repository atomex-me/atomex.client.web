// using Atomex.Core;
//
// namespace atomex_frontend.atomex_data_structures
// {
//   public class WalletAddressView
//   {
//     public WalletAddress WalletAddress { get; }
//     public string Address => WalletAddress.Address;
//     public decimal AvailableBalance => WalletAddress.AvailableBalance();
//     public string CurrencyFormat { get; }
//     public bool IsFreeAddress { get; }
//
//     public WalletAddressView(
//         WalletAddress walletAddress,
//         string currencyFormat,
//         bool isFreeAddress = false)
//     {
//       WalletAddress = walletAddress;
//       CurrencyFormat = currencyFormat;
//       IsFreeAddress = isFreeAddress;
//     }
//   }
// }