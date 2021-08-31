using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Atomex;
using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Services;
using Atomex.Swaps.Helpers;
using Atomex.Swaps;
using Serilog;
using Microsoft.JSInterop;
using atomex_frontend.Common;
using atomex_frontend.I18nText;
using Atomex.Wallet.Abstract;

namespace atomex_frontend.Storages
{
    public class SwapStorage
    {
        public SwapStorage(
            AccountStorage accountStorage,
            WalletStorage walletStorage,
            Toolbelt.Blazor.I18nText.I18nText I18nText,
            IJSRuntime JSRuntime)
        {
            this.jSRuntime = JSRuntime;
            this.accountStorage = accountStorage;
            this.walletStorage = walletStorage;

            this.accountStorage.InitializeCallback += SubscribeToServices;
            this.walletStorage.RefreshMarket += (bool reset) => _ = UpdateAmountAsync(reset ? 0 : _amount);
            LoadTranslations(I18nText);
        }

        private IJSRuntime jSRuntime;
        Translations Translations = new Translations();

        private async void LoadTranslations(Toolbelt.Blazor.I18nText.I18nText I18nText)
        {
            Translations = await I18nText.GetTextTableAsync<I18nText.Translations>(null);
        }

        public event Action RefreshRequested;

        public void CallUIRefresh()
        {
            RefreshRequested?.Invoke();
        }

        private AccountStorage accountStorage;
        private WalletStorage walletStorage;

        private IAtomexApp App
        {
            get => accountStorage.AtomexApp;
        }

        public CurrencyConfig FromCurrency
        {
            get => walletStorage.SelectedCurrency;
        }


        public CurrencyConfig ToCurrency
        {
            get => walletStorage.SelectedSecondCurrency;
        }

        public string CurrencyFormat
        {
            get => walletStorage.SelectedCurrency.Format;
        }

        private String CurrencyCode
        {
            get => FromCurrency.Name;
        }

        public String TargetCurrencyCode
        {
            get => ToCurrency.Name;
        }

        public String BaseCurrencyCode => "USD";

        private ISymbols Symbols
        {
            get => App.SymbolsProvider.GetSymbols(App.Account.Network);
        }

        private ICurrencies Currencies
        {
            get => App.Account.Currencies;
        }

        private decimal _estimatedOrderPrice;

        private decimal EstimatedOrderPrice
        {
            get => _estimatedOrderPrice;
        }

        private decimal _estimatedPrice;

        public decimal EstimatedPrice
        {
            get => _estimatedPrice;
            set { _estimatedPrice = value; }
        }

        private decimal _estimatedPaymentFee;

        public decimal EstimatedPaymentFee
        {
            get => _estimatedPaymentFee;
            set { _estimatedPaymentFee = value; }
        }

        public string FromFeeCurrencyFormat
        {
            get => FromCurrency.FeeCurrencyName;
        }

        public decimal EstimatedPaymentFeeInBase
        {
            get => walletStorage.GetDollarValue(FromFeeCurrencyFormat, _estimatedPaymentFee);
        }

        private bool _isAmountValid = true;

        public bool IsAmountValid
        {
            get => _isAmountValid;
            set { _isAmountValid = value; }
        }

        private decimal _amount;

        public decimal Amount
        {
            get => _amount;
            set
            {
                _amount = value;
                _ = UpdateAmountAsync(_amount, updateUi: false);
            }
        }

        public string FromAmountString
        {
            get => _amount.ToString(CurrencyFormat, CultureInfo.InvariantCulture);
        }

        public decimal AmountInBase
        {
            get => walletStorage.GetDollarValue(CurrencyCode, _amount);
        }

        private decimal _targetAmount;

        public decimal TargetAmount
        {
            get => _targetAmount;
            set { _targetAmount = value; }
        }

        public decimal TargetAmountInBase
        {
            get => walletStorage.GetDollarValue(TargetCurrencyCode, _targetAmount);
        }

        private bool _isAmountUpdating;

        public bool IsAmountUpdating
        {
            get => _isAmountUpdating;
            set { _isAmountUpdating = value; }
        }

        public decimal AmountDollars
        {
            get => walletStorage.GetDollarValue(walletStorage.SelectedCurrency.Name, this._amount);
        }

        public decimal TargetAmountDollars
        {
            get => walletStorage.GetDollarValue(walletStorage.SelectedSecondCurrency.Name, this._targetAmount);
        }

        private decimal _estimatedMaxAmount;

        public decimal EstimatedMaxAmount
        {
            get => _estimatedMaxAmount;
            set { _estimatedMaxAmount = value; }
        }

        public string TargetFeeCurrencyFormat
        {
            get => ToCurrency.FeeCurrencyName;
        }

        private decimal _estimatedRedeemFee;

        public decimal EstimatedRedeemFee
        {
            get => _estimatedRedeemFee;
            set { _estimatedRedeemFee = value; }
        }

        public decimal EstimatedRedeemFeeInBase
        {
            get => walletStorage.GetDollarValue(TargetFeeCurrencyFormat, _estimatedRedeemFee);
        }

        private decimal _estimatedMakerNetworkFee;

        public decimal EstimatedMakerNetworkFee
        {
            get => _estimatedMakerNetworkFee;
            set { _estimatedMakerNetworkFee = value; }
        }

        // private decimal _estimatedMakerNetworkFeeInBase;
        public decimal EstimatedMakerNetworkFeeInBase
        {
            get => walletStorage.GetDollarValue(CurrencyCode, _estimatedMakerNetworkFee);
        }

        public decimal EstimatedTotalNetworkFeeInBase
        {
            get => EstimatedPaymentFeeInBase +
                   (!_hasRewardForRedeem ? EstimatedRedeemFeeInBase : 0) +
                   EstimatedMakerNetworkFeeInBase +
                   (_hasRewardForRedeem ? RewardForRedeemInBase : 0);
        }

        private decimal _rewardForRedeem;

        public decimal RewardForRedeem
        {
            get => _rewardForRedeem;
            set { _rewardForRedeem = value; }
        }

        public decimal RewardForRedeemInBase
        {
            get => walletStorage.GetDollarValue(TargetCurrencyCode, _rewardForRedeem);
        }

        private bool _hasRewardForRedeem;

        public bool HasRewardForRedeem
        {
            get => _hasRewardForRedeem;
            set { _hasRewardForRedeem = value; }
        }

        private bool _isNoLiquidity;

        public bool IsNoLiquidity
        {
            get => _isNoLiquidity;
            set { _isNoLiquidity = value; }
        }

        protected string _warning;

        public string Warning
        {
            get => _warning;
            set { _warning = value; }
        }

        protected bool _isCriticalWarning;

        public bool IsCriticalWarning
        {
            get => _isCriticalWarning;
            set { _isCriticalWarning = value; }
        }

        private bool _canConvert = true;

        public bool CanConvert
        {
            get => _canConvert;
            set { _canConvert = value; }
        }

        private long lastIncompletedSwapId;

        private static TimeSpan SwapTimeout = TimeSpan.FromSeconds(60);
        private static TimeSpan SwapCheckInterval = TimeSpan.FromSeconds(3);
        public IEnumerable<Swap> Swaps { get; set; } = new List<Swap>();

        private void SubscribeToServices(bool IsRestarting)
        {
            if (!IsRestarting)
            {
                App.AtomexClientChanged += OnAtomexClientChanged;
                App.Terminal.QuotesUpdated += (object sender, MarketDataEventArgs args) =>
                    _ = OnQuotesUpdatedEventHandler(sender, args);
                App.Terminal.SwapUpdated += OnSwapEventHandler;

                if (App.HasQuotesProvider)
                    App.QuotesProvider.QuotesUpdated += (object sender, EventArgs args) =>
                    {
                        OnBaseQuotesUpdatedEventHandler(sender, args);
                        this.CallUIRefresh();
                    };
                
                Console.WriteLine("Subscribed to swap events in start");
            }

            OnSwapEventHandler(this, null);
        }

        // private void OnTerminalServiceStateChangedEventHandler(object sender, TerminalServiceEventArgs args)
        // {
        //   if (!(sender is IAtomexClient terminal))
        //     return;

        //   if (args.Service == TerminalService.MarketData)
        //   {
        //     if (Amount != 0)
        //     {
        //       Console.WriteLine($"UPDATING AMOUNT TO 0 DUE TO DISCONNECT");
        //       _ = UpdateAmountAsync(0, true);
        //     }
        //   }
        // }

        private void OnAtomexClientChanged(object sender, AtomexClientChangedEventArgs args)
        {
            Console.WriteLine("SWAP: TERMINAL CHANGED EVENT");
            var terminal = args.AtomexClient;

            if (terminal?.Account == null)
                return;

            terminal.QuotesUpdated += (object sender, MarketDataEventArgs args) =>
                _ = OnQuotesUpdatedEventHandler(sender, args);
            terminal.SwapUpdated += OnSwapEventHandler;
            Console.WriteLine("Subscribed to swap events");
            OnSwapEventHandler(this, null);
        }

        protected void OnBaseQuotesUpdatedEventHandler(object sender, EventArgs args)
        {
            if (!(sender is ICurrencyQuotesProvider provider))
                return;


            if (CurrencyCode == null || TargetCurrencyCode == null || BaseCurrencyCode == null)
                return;

            if (AmountInBase != 0 && EstimatedTotalNetworkFeeInBase / AmountInBase > 0.3m)
            {
                IsCriticalWarning = true;
                Warning = string.Format(
                    CultureInfo.InvariantCulture,
                    Translations.CvTooHighNetworkFee,
                    FormattableString.Invariant($"{EstimatedTotalNetworkFeeInBase:$0.00}"),
                    FormattableString.Invariant($"{EstimatedTotalNetworkFeeInBase / AmountInBase:0.00%}"));
            }
            else if (AmountInBase != 0 && EstimatedTotalNetworkFeeInBase / AmountInBase > 0.1m)
            {
                IsCriticalWarning = false;
                Warning = string.Format(
                    CultureInfo.InvariantCulture,
                    Translations.CvSufficientNetworkFee,
                    FormattableString.Invariant($"{EstimatedTotalNetworkFeeInBase:$0.00}"),
                    FormattableString.Invariant($"{EstimatedTotalNetworkFeeInBase / AmountInBase:0.00%}"));
            }

            CanConvert = AmountInBase == 0 || EstimatedTotalNetworkFeeInBase / AmountInBase <= 0.75m;
        }

        public async Task OnQuotesUpdatedEventHandler(object sender, MarketDataEventArgs args)
        {
            try
            {
                var swapPriceEstimation = await Atomex.ViewModels.Helpers.EstimateSwapPriceAsync(
                    amount: Amount,
                    fromCurrency: FromCurrency,
                    toCurrency: ToCurrency,
                    account: App.Account,
                    atomexClient: App.Terminal,
                    symbolsProvider: App.SymbolsProvider);

                if (swapPriceEstimation == null)
                    return;

                _targetAmount = swapPriceEstimation.TargetAmount;
                _estimatedPrice = swapPriceEstimation.Price;
                _estimatedOrderPrice = swapPriceEstimation.OrderPrice;
                _estimatedMaxAmount = swapPriceEstimation.MaxAmount;
                _isNoLiquidity = swapPriceEstimation.IsNoLiquidity;
            }
            catch (Exception e)
            {
                Log.Error(e, "Quotes updated event handler error");
            }
            finally
            {
                this.CallUIRefresh();
            }
        }

        private async void OnSwapEventHandler(object sender, SwapEventArgs args)
        {
            try
            {
                Swaps = (await App.Account
                        .GetSwapsAsync())
                    .ToList()
                    .OrderByDescending(sw => sw.TimeStamp.ToUniversalTime());

                Console.WriteLine($"Finded {Swaps.Count()} swaps");

                if (args != null)
                {
                    if (!args.Swap.IsComplete)
                    {
                        lastIncompletedSwapId = args.Swap.Id;
                    }
                    else
                    {
                        if (args.Swap.Id == lastIncompletedSwapId)
                        {
                            var description =
                                $"Converting {AmountHelper.QtyToAmount(args.Swap.Side, args.Swap.Qty, args.Swap.Price, getCurrency(args.Swap.SoldCurrency).DigitsMultiplier)} {args.Swap.SoldCurrency} to {AmountHelper.QtyToAmount(args.Swap.Side.Opposite(), args.Swap.Qty, args.Swap.Price, getCurrency(args.Swap.PurchasedCurrency).DigitsMultiplier)} {args.Swap.PurchasedCurrency} successfully completed";
                            Console.WriteLine(description);
                            jSRuntime.InvokeVoidAsync("showNotification", $"Swap completed", description, null);
                        }
                    }
                }

                this.CallUIRefresh();
            }
            catch (Exception e)
            {
                Log.Error($"Swaps update error {e.ToString()}");
            }
        }

        private CurrencyConfig getCurrency(string Currency)
        {
            return AccountStorage.Currencies.GetByName(Currency);
        }

        public async void MaxAmountCommand()
        {
            try
            {
                var swapParams = await Atomex.ViewModels.Helpers
                    .EstimateSwapPaymentParamsAsync(
                        amount: EstimatedMaxAmount,
                        fromCurrency: FromCurrency,
                        toCurrency: ToCurrency,
                        account: App.Account,
                        atomexClient: App.Terminal,
                        symbolsProvider: App.SymbolsProvider);

                _amount = Math.Min(swapParams.Amount, EstimatedMaxAmount);
                _ = UpdateAmountAsync(_amount, updateUi: true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Max amount command error.");
            }
        }

        protected virtual async Task UpdateAmountAsync(decimal value, bool updateUi = false)
        {
            Warning = string.Empty;
            Console.WriteLine($"Updating amount with {value}");

            try
            {
                if (value == 0)
                {
                    _amount = value;
                    this.CallUIRefresh();
                }

                IsAmountUpdating = true;
                // esitmate max payment amount and max fee
                var swapParams = await Atomex.ViewModels.Helpers
                    .EstimateSwapPaymentParamsAsync(
                        amount: value,
                        fromCurrency: FromCurrency,
                        toCurrency: ToCurrency,
                        account: App.Account,
                        atomexClient: App.Terminal,
                        symbolsProvider: App.SymbolsProvider);

                IsCriticalWarning = false;

                if (swapParams.Error != null)
                {
                    Warning = swapParams.Error.Code switch
                    {
                        Errors.InsufficientFunds => Translations.CvInsufficientFunds,
                        Errors.InsufficientChainFunds => string.Format(CultureInfo.InvariantCulture,
                            Translations.CvInsufficientChainFunds, FromCurrency.FeeCurrencyName),
                        _ => Translations.CvError
                    };
                }
                else
                {
                    Warning = string.Empty;
                }

                // _amount = swapParams.Amount;
                _estimatedPaymentFee = swapParams.PaymentFee;
                _estimatedMakerNetworkFee = swapParams.MakerNetworkFee;

                IsAmountValid = _amount <= Helper.TruncateByFormat(swapParams.Amount, CurrencyFormat);

                // if (updateUi)
                //     OnPropertyChanged(nameof(AmountString));

                await UpdateRedeemAndRewardFeesAsync();

                OnBaseQuotesUpdatedEventHandler(App.QuotesProvider, EventArgs.Empty);
                await OnQuotesUpdatedEventHandler(App.Terminal, null);
            }
            finally
            {
                IsAmountUpdating = false;
            }
        }

        private async Task UpdateRedeemAndRewardFeesAsync()
        {
            var walletAddress = await App.Account
                .GetCurrencyAccount<ILegacyCurrencyAccount>(ToCurrency.Name)
                .GetRedeemAddressAsync();
            _estimatedRedeemFee = await ToCurrency
                .GetEstimatedRedeemFeeAsync(walletAddress, withRewardForRedeem: false);

            _rewardForRedeem = await RewardForRedeemHelper
                .EstimateAsync(
                    account: App.Account,
                    quotesProvider: App.QuotesProvider,
                    feeCurrencyQuotesProvider: symbol => App.Terminal?.GetOrderBook(symbol)?.TopOfBook(),
                    walletAddress: walletAddress);

            _hasRewardForRedeem = _rewardForRedeem != 0;
        }

        public async Task<string> Send()
        {
            try
            {
                var error = await ConvertAsync();

                if (error != null)
                {
                    Console.WriteLine(error.Description); // todo: go back to confirmation;
                    return error?.Description ?? "An error has occurred while sending swap.";
                }

                Console.WriteLine("Swap successfully created");
                OnSuccessConvertion();

                return null;
            }
            catch (Exception)
            {
                return "An error has occurred while sending swap.";
            }
        }

        private void OnSuccessConvertion()
        {
            Console.WriteLine("Calling OnSuccessConvertion");
            _amount = Math.Min(_amount, EstimatedMaxAmount); // recalculate amount
            _ = UpdateAmountAsync(_amount, updateUi: true);
        }

        private async Task<Error> ConvertAsync()
        {
            try
            {
                var account = App.Account;
                var currencyAccount = account
                    .GetCurrencyAccount<ILegacyCurrencyAccount>(FromCurrency.Name);

                var fromWallets = (await currencyAccount
                        .GetUnspentAddressesAsync(
                            toAddress: null,
                            amount: Amount,
                            fee: 0,
                            feePrice: await FromCurrency.GetDefaultFeePriceAsync(),
                            feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                            addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                            transactionType: BlockchainTransactionType.SwapPayment))
                    .ToList();
                
                foreach (var fromWallet in fromWallets)
                    if (fromWallet.Currency != FromCurrency.Name)
                        fromWallet.Currency = FromCurrency.Name;

                if (Amount == 0)
                    return new Error(Errors.SwapError, Translations.CvZeroAmount);

                if (Amount > 0 && !fromWallets.Any())
                    return new Error(Errors.SwapError, Translations.CvInsufficientFunds);

                var symbol = App.SymbolsProvider
                    .GetSymbols(App.Account.Network)
                    .SymbolByCurrencies(FromCurrency, ToCurrency);

                var baseCurrency = App.Account.Currencies.GetByName(symbol.Base);
                var side = symbol.OrderSideForBuyCurrency(ToCurrency);
                var terminal = App.Terminal;
                var price = EstimatedPrice;
                var orderPrice = EstimatedOrderPrice;

                if (price == 0)
                    return new Error(Errors.NoLiquidity, Translations.CvNoLiquidity);

                var qty = AmountHelper.AmountToQty(side, Amount, price, baseCurrency.DigitsMultiplier);

                if (qty < symbol.MinimumQty)
                {
                    var minimumAmount =
                        AmountHelper.QtyToAmount(side, symbol.MinimumQty, price, FromCurrency.DigitsMultiplier);
                    var message = string.Format(CultureInfo.InvariantCulture, Translations.CvMinimumAllowedQtyWarning,
                        minimumAmount, FromCurrency.Name);

                    return new Error(Errors.SwapError, message);
                }

                var order = new Order
                {
                    Symbol = symbol.Name,
                    TimeStamp = DateTime.UtcNow,
                    Price = orderPrice,
                    Qty = qty,
                    Side = side,
                    Type = OrderType.FillOrKill,
                    FromWallets = fromWallets.ToList(),
                    MakerNetworkFee = EstimatedMakerNetworkFee
                };

                await order.CreateProofOfPossessionAsync(account);

                terminal.OrderSendAsync(order);

                // wait for swap confirmation
                var timeStamp = DateTime.UtcNow;

                while (DateTime.UtcNow < timeStamp + SwapTimeout)
                {
                    await Task.Delay(SwapCheckInterval);

                    var currentOrder = terminal.Account.GetOrderById(order.ClientOrderId);

                    if (currentOrder == null)
                        continue;

                    if (currentOrder.Status == OrderStatus.Pending)
                        continue;

                    if (currentOrder.Status == OrderStatus.PartiallyFilled || currentOrder.Status == OrderStatus.Filled)
                    {
                        var swap = (await terminal.Account
                                .GetSwapsAsync())
                            .FirstOrDefault(s => s.OrderId == currentOrder.Id);

                        if (swap == null)
                            continue;

                        return null;
                    }

                    if (currentOrder.Status == OrderStatus.Canceled)
                        return new Error(Errors.PriceHasChanged, Translations.SvPriceHasChanged);

                    if (currentOrder.Status == OrderStatus.Rejected)
                        return new Error(Errors.OrderRejected, Translations.SvOrderRejected);
                }

                return new Error(Errors.TimeoutReached, Translations.SvTimeoutReached);
            }
            catch (Exception e)
            {
                Log.Error(e, "Conversion error");

                return new Error(Errors.SwapError, Translations.CvConversionError);
            }
        }

        public string NotReadyConvertMessage
        {
            get
            {
                if (_amount == 0)
                {
                    return Translations.CvZeroAmount;
                }

                if (!IsAmountValid)
                {
                    return Translations.CvBigAmount;
                }

                if (EstimatedPrice == 0)
                {
                    return Translations.CvNoLiquidity;
                }

                if (!App.Terminal.IsServiceConnected(TerminalService.All))
                {
                    return Translations.CvServicesUnavailable;
                }

                var symbol = Symbols.SymbolByCurrencies(FromCurrency, ToCurrency);
                if (symbol == null)
                {
                    return Translations.CvNotSupportedSymbol;
                }

                var side = symbol.OrderSideForBuyCurrency(ToCurrency);
                var price = EstimatedPrice;
                var baseCurrency = Currencies.GetByName(symbol.Base);
                var qty = AmountHelper.AmountToQty(side, _amount, price, baseCurrency.DigitsMultiplier);

                if (qty < symbol.MinimumQty)
                {
                    var minimumAmount =
                        AmountHelper.QtyToAmount(side, symbol.MinimumQty, price, FromCurrency.DigitsMultiplier);
                    var message = string.Format(CultureInfo.InvariantCulture, Translations.CvMinimumAllowedQtyWarning,
                        minimumAmount, FromCurrency.Name);

                    return message;
                }

                return null;
            }
        }
    }
}