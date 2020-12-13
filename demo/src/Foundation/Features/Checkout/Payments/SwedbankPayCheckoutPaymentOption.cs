﻿using EPiServer.Commerce.Order;
using EPiServer.Framework.Localization;
using EPiServer.ServiceLocation;

using Foundation.Commerce.Markets;
using Foundation.Features.Checkout.Services;

using Mediachase.Commerce;
using Mediachase.Commerce.Markets;
using Mediachase.Commerce.Orders;

using SwedbankPay.Episerver.Checkout;
using SwedbankPay.Episerver.Checkout.Common;
using SwedbankPay.Episerver.Checkout.Common.Extensions;
using SwedbankPay.Sdk;
using SwedbankPay.Sdk.PaymentOrders;

using System;
using System.ComponentModel;
using System.Linq;

using TransactionType = Mediachase.Commerce.Orders.TransactionType;

namespace Foundation.Features.Checkout.Payments
{
    [ServiceConfiguration(typeof(IPaymentMethod))]
    public class SwedbankPayCheckoutPaymentOption : PaymentOptionBase, IDataErrorInfo
    {
        private readonly IOrderGroupFactory _orderGroupFactory;
        private readonly ICartService _cartService;
        private readonly LanguageService _languageService;
        private readonly IMarketService _marketService;
        private readonly ISwedbankPayCheckoutService _swedbankPayCheckoutService;
        private bool _isInitalized;

        public SwedbankPayCheckoutPaymentOption() : this(
            LocalizationService.Current,
            ServiceLocator.Current.GetInstance<IOrderGroupFactory>(),
            ServiceLocator.Current.GetInstance<ICartService>(),
            ServiceLocator.Current.GetInstance<ICurrentMarket>(),
            ServiceLocator.Current.GetInstance<LanguageService>(),
            ServiceLocator.Current.GetInstance<IMarketService>(),
            ServiceLocator.Current.GetInstance<IPaymentService>(),
            ServiceLocator.Current.GetInstance<ISwedbankPayCheckoutService>())
        {
        }

        public SwedbankPayCheckoutPaymentOption(
            LocalizationService localizationService,
            IOrderGroupFactory orderGroupFactory,
            ICartService cartService,
            ICurrentMarket currentMarket,
            LanguageService languageService,
            IMarketService marketService,
            IPaymentService paymentService,
            ISwedbankPayCheckoutService swedbankPayCheckoutService) : base(localizationService, orderGroupFactory, currentMarket, languageService, paymentService)
        {
            _orderGroupFactory = orderGroupFactory;
            _cartService = cartService;
            _languageService = languageService;
            _marketService = marketService;
            _swedbankPayCheckoutService = swedbankPayCheckoutService;
        }

        public void InitializeValues()
        {
            var cart = _cartService.LoadCart(_cartService.DefaultCartName, true)?.Cart;
            InitializeValues(cart);
        }

        public void InitializeValues(ICart cart)
        {
            if (_isInitalized || cart == null)
            {
                return;
            }

            var market = _marketService.GetMarket(cart.MarketId);

            var currentLanguage = _languageService.GetCurrentLanguage();
            Culture = currentLanguage.TextInfo.CultureName;
            CheckoutConfiguration = _swedbankPayCheckoutService.LoadCheckoutConfiguration(market, currentLanguage.TwoLetterISOLanguageName);

            var orderId = cart.Properties[Constants.SwedbankPayOrderIdField]?.ToString();
            if (!string.IsNullOrWhiteSpace(orderId) || CheckoutConfiguration.UseAnonymousCheckout)
            {
                GetCheckoutJavascriptSource(cart, $"description cart {cart.OrderLink.OrderGroupId}");
            }
            else
            {
                GetCheckInJavascriptSource(cart);
            }

            _isInitalized = true;
        }

        private void GetCheckoutJavascriptSource(ICart cart, string description)
        {
            var orderData = _swedbankPayCheckoutService.CreateOrUpdatePaymentOrder(cart, description);
            JavascriptSource = orderData.Operations.View?.Href;
            UseCheckoutSource = true;
        }

        private void GetCheckInJavascriptSource(ICart cart)
        {
            string email = "PayexTester@payex.com";
            string phone = "+46739000001";
            string ssn = "199710202392";

            var orderData = _swedbankPayCheckoutService.InitiateConsumerSession(_languageService.GetCurrentLanguage(), email, phone, ssn);
            JavascriptSource = orderData.Operations.ViewConsumerIdentification?.Href;
        }

        public override IPayment CreatePayment(decimal amount, IOrderGroup orderGroup)
        {
            var paymentOrder = _swedbankPayCheckoutService.GetPaymentOrder(orderGroup, PaymentOrderExpand.All);
            var currentPayment = paymentOrder.PaymentOrderResponse.CurrentPayment.Payment;
            var transaction = currentPayment?.Transactions?.TransactionList?.FirstOrDefault();
            var transactionType = transaction?.Type.ConvertToEpiTransactionType() ?? TransactionType.Authorization;

            var payment = orderGroup.CreatePayment(_orderGroupFactory);
            payment.PaymentType = PaymentType.Other;
            payment.PaymentMethodId = PaymentMethodId;
            payment.PaymentMethodName = Constants.SwedbankPayCheckoutSystemKeyword;
            payment.ProviderTransactionID = transaction?.Number;
            payment.Amount = amount;
            var isSwishPayment = currentPayment?.Instrument.Equals(PaymentInstrument.Swish) ?? false;
            payment.Status = isSwishPayment ? PaymentStatus.Processed.ToString() : PaymentStatus.Pending.ToString();

            payment.TransactionType = transactionType.ToString();

            return payment;
        }

        public override bool ValidateData() => true;
        public override string SystemKeyword => Constants.SwedbankPayCheckoutSystemKeyword;
        public string this[string columnName] => string.Empty;
        public string Error { get; }
        public CheckoutConfiguration CheckoutConfiguration { get; set; }
        public string Culture { get; set; }
        public Uri JavascriptSource { get; set; }
        public bool UseCheckoutSource { get; set; }
    }
}