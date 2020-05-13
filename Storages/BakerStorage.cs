// using System;
// using System.Linq;
// using System.Globalization;
// using Atomex.Wallet;
// using Microsoft.AspNetCore.Components;
// using Blazored.LocalStorage;
// using Microsoft.Extensions.Configuration;
// using Microsoft.JSInterop;
// using System.IO;
// using System.Reflection;
// using Atomex.Common.Configuration;
// using Atomex;
// using Atomex.MarketData.Bitfinex;
// using Atomex.MarketData.Abstract;
// using Atomex.Core;
// using System.Security;
// using Newtonsoft.Json;
// using System.Collections.Generic;
// using System.Threading.Tasks;
// using Atomex.Blockchain;
// using Atomex.Abstract;
// using Atomex.Subsystems.Abstract;
// using Atomex.MarketData;
// using atomex_frontend.atomex_data_structures;
// using atomex_frontend.Common;
// using Atomex.Blockchain.Tezos;

// namespace atomex_frontend.Storages
// {
//   public class BakerStorage
//   {
//     BakerStorage(AccountStorage AS, Toolbelt.Blazor.I18nText.I18nText I18nText)
//     {
//       this.accountStorage = AS;
//       GetTrans(I18nText);
//     }

//     private async void GetTrans(Toolbelt.Blazor.I18nText.I18nText I18nText)
//     {
//       Translations = await I18nText.GetTextTableAsync<I18nText.Translations>(null);
//     }

//     I18nText.Translations Translations = new I18nText.Translations();

//     private AccountStorage accountStorage;
//     private IAtomexApp App { get => accountStorage.AtomexApp; }

//     private readonly Tezos _tezos;
//     private WalletAddress _walletAddress;
//     private TezosTransaction _tx;

//     public WalletAddress WalletAddress
//     {
//       get => _walletAddress;
//       set
//       {
//         _walletAddress = value;
//       }
//     }

//     private List<Baker> _fromBakersList;
//     public List<Baker> FromBakersList
//     {
//       get => _fromBakersList;
//       private set
//       {
//         _fromBakersList = value;

//         Baker = FromBakersList.FirstOrDefault();
//       }
//     }

//     private List<WalletAddress> _fromAddressList;
//     public List<WalletAddress> FromAddressList
//     {
//       get => _fromAddressList;
//       private set
//       {
//         _fromAddressList = value;

//         WalletAddress = FromAddressList.FirstOrDefault();
//       }
//     }

//     private Baker _baker;
//     public Baker Baker
//     {
//       get => _baker;
//       set
//       {
//         _baker = value;

//         if (_baker != null)
//           Address = _baker.Address;
//       }
//     }

//     public string FeeString
//     {
//       get => Fee.ToString(_tezos.FeeFormat, CultureInfo.InvariantCulture);
//       set
//       {
//         if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fee))
//           return;

//         Fee = Helper.TruncateByFormat(fee, _tezos.FeeFormat);
//       }
//     }

//     private decimal _fee;
//     public decimal Fee
//     {
//       get => _fee;
//       set
//       {
//         _fee = value;

//         if (!UseDefaultFee)
//         {
//           var feeAmount = _fee;

//           if (feeAmount > _walletAddress.Balance)
//             feeAmount = _walletAddress.Balance;

//           _fee = feeAmount;

//           Warning = string.Empty;
//         }

//         OnQuotesUpdatedEventHandler(App.QuotesProvider, EventArgs.Empty);
//       }
//     }

//     private string _baseCurrencyFormat;
//     public string BaseCurrencyFormat
//     {
//       get => _baseCurrencyFormat;
//       set { _baseCurrencyFormat = value; }
//     }

//     private decimal _feeInBase;
//     public decimal FeeInBase
//     {
//       get => _feeInBase;
//       set { _feeInBase = value; }
//     }

//     private string _feeCurrencyCode;
//     public string FeeCurrencyCode
//     {
//       get => _feeCurrencyCode;
//       set { _feeCurrencyCode = value; }
//     }

//     private string _baseCurrencyCode;
//     public string BaseCurrencyCode
//     {
//       get => _baseCurrencyCode;
//       set { _baseCurrencyCode = value; }
//     }

//     private bool _useDefaultFee;
//     public bool UseDefaultFee
//     {
//       get => _useDefaultFee;
//       set
//       {
//         _useDefaultFee = value;
//       }
//     }

//     private string _address;
//     public string Address
//     {
//       get => _address;
//       set
//       {
//         _address = value;

//         var baker = FromBakersList.FirstOrDefault(b => b.Address == _address);

//         if (baker == null)
//           Baker = null;
//         else if (baker != Baker)
//           Baker = baker;
//       }
//     }

//     private string _warning;
//     public string Warning
//     {
//       get => _warning;
//     }

//     private bool _delegationCheck;
//     public bool DelegationCheck
//     {
//       get => _delegationCheck;
//       set { _delegationCheck = value; }
//     }


//     public void NextCommand()
//     {
//       if (DelegationCheck)
//         return;

//       DelegationCheck = true;

//       try
//       {
//         if (string.IsNullOrEmpty(Address))
//         {
//           Warning = Resources.SvEmptyAddressError;
//           return;
//         }

//         if (!_tezos.IsValidAddress(Address))
//         {
//           Warning = Resources.SvInvalidAddressError;
//           return;
//         }

//         if (Fee < 0)
//         {
//           Warning = Resources.SvCommissionLessThanZeroError;
//           return;
//         }

//         /*
//         if (xTezos.GetFeeAmount(Fee, FeePrice) > CurrencyViewModel.AvailableAmount) {
//             Warning = Resources.SvAvailableFundsError;
//             return;
//         }*/

//         var result = await GetDelegate();

//         if (result.HasError)
//           Warning = result.Error.Description;
//         else
//         {
//           var confirmationViewModel = new DelegateConfirmationViewModel(DialogViewer, _onDelegate)
//           {
//             Currency = _tezos,
//             WalletAddress = WalletAddress,
//             UseDefaultFee = UseDefaultFee,
//             Tx = _tx,
//             From = WalletAddress.Address,
//             To = Address,
//             IsAmountLessThanMin = WalletAddress.Balance < (BakerViewModel?.MinDelegation ?? 0),
//             BaseCurrencyCode = BaseCurrencyCode,
//             BaseCurrencyFormat = BaseCurrencyFormat,
//             Fee = Fee,
//             FeeInBase = FeeInBase,
//             CurrencyCode = _tezos.FeeCode,
//             CurrencyFormat = _tezos.FeeFormat
//           };

//           DialogViewer.PushPage(Dialogs.Delegate, Pages.DelegateConfirmation, confirmationViewModel);
//         }
//       }
//       finally
//       {
//         DelegationCheck = false;
//       }
//     }

//     private readonly Action _onDelegate;

//     public DelegateViewModel()
//     {
// #if DEBUG
//       if (Env.IsInDesignerMode())
//         DesignerMode();
// #endif
//     }

//     public DelegateViewModel(
//         IAtomexApp app,
//         IDialogViewer dialogViewer,
//         Action onDelegate = null)
//     {
//       App = app ?? throw new ArgumentNullException(nameof(app));
//       DialogViewer = dialogViewer ?? throw new ArgumentNullException(nameof(dialogViewer));
//       _onDelegate = onDelegate;

//       _tezos = App.Account.Currencies.Get<Tezos>("XTZ");
//       FeeCurrencyCode = _tezos.FeeCode;
//       BaseCurrencyCode = "USD";
//       BaseCurrencyFormat = "$0.00";
//       UseDefaultFee = true;

//       SubscribeToServices();
//       LoadBakerList().FireAndForget();
//       PrepareWallet().WaitForResult();
//     }

//     private async Task LoadBakerList()
//     {
//       List<BakerViewModel> bakers = null;

//       try
//       {
//         await Task.Run(async () =>
//         {
//           bakers = (await BbApi
//                       .GetBakers(App.Account.Network)
//                       .ConfigureAwait(false))
//                       .Select(x => new BakerViewModel
//                       {
//                         Address = x.Address,
//                         Logo = x.Logo,
//                         Name = x.Name,
//                         Fee = x.Fee,
//                         MinDelegation = x.MinDelegation,
//                         StakingAvailable = x.StakingAvailable
//                       })
//                       .ToList();
//         });
//       }
//       catch (Exception e)
//       {
//         Log.Error(e.Message, "Error while fetching bakers list");
//       }

//       await Application.Current.Dispatcher.InvokeAsync(() =>
//       {
//         FromBakersList = bakers;
//       }, DispatcherPriority.Background);
//     }

//     private async Task PrepareWallet(CancellationToken cancellationToken = default)
//     {
//       FromAddressList = (await App.Account
//           .GetUnspentAddressesAsync(_tezos.Name, cancellationToken).ConfigureAwait(false))
//           .OrderByDescending(x => x.Balance)
//           .Select(w => new WalletAddressViewModel(w, _tezos.Format))
//           .ToList();

//       if (!FromAddressList?.Any() ?? false)
//       {
//         Warning = "You don't have non-empty accounts";
//         return;
//       }

//       WalletAddress = FromAddressList.FirstOrDefault().WalletAddress;
//     }

//     private async Task<Result<string>> GetDelegate(
//         CancellationToken cancellationToken = default)
//     {
//       if (_walletAddress == null)
//         return new Error(Errors.InvalidWallets, "You don't have non-empty accounts");

//       var wallet = (HdWallet)App.Account.Wallet;
//       var keyStorage = wallet.KeyStorage;
//       var rpc = new Rpc(_tezos.RpcNodeUri);

//       JObject delegateData;
//       try
//       {
//         delegateData = await rpc
//             .GetDelegate(_address)
//             .ConfigureAwait(false);
//       }
//       catch
//       {
//         return new Error(Errors.WrongDelegationAddress, "Wrong delegation address");
//       }

//       if (delegateData["deactivated"].Value<bool>())
//         return new Error(Errors.WrongDelegationAddress, "Baker is deactivated. Pick another one");

//       var delegators = delegateData["delegated_contracts"]?.Values<string>();

//       if (delegators.Contains(_walletAddress.Address))
//         return new Error(Errors.AlreadyDelegated, $"Already delegated from {_walletAddress.Address} to {_address}");

//       var tx = new TezosTransaction
//       {
//         StorageLimit = _tezos.StorageLimit,
//         GasLimit = _tezos.GasLimit,
//         From = _walletAddress.Address,
//         To = _address,
//         Fee = Fee.ToMicroTez(),
//         Currency = _tezos,
//         CreationTime = DateTime.UtcNow,
//       };

//       try
//       {
//         var calculatedFee = await tx.AutoFillAsync(keyStorage, _walletAddress, UseDefaultFee);
//         if (!calculatedFee)
//           return new Error(Errors.TransactionCreationError, $"Autofill transaction failed");

//         Fee = tx.Fee;
//         _tx = tx;
//       }
//       catch (Exception e)
//       {
//         Log.Error(e, "Autofill delegation error");
//         return new Error(Errors.TransactionCreationError, $"Autofill delegation error. Try again later");
//       }

//       return "Successful check";
//     }

//     private void SubscribeToServices()
//     {
//       if (App.HasQuotesProvider)
//         App.QuotesProvider.QuotesUpdated += OnQuotesUpdatedEventHandler;
//     }

//     private void OnQuotesUpdatedEventHandler(object sender, EventArgs args)
//     {
//       if (!(sender is ICurrencyQuotesProvider quotesProvider))
//         return;

//       var quote = quotesProvider.GetQuote(FeeCurrencyCode, BaseCurrencyCode);

//       if (quote != null)
//         FeeInBase = Fee * quote.Bid;
//     }


//   }
// }