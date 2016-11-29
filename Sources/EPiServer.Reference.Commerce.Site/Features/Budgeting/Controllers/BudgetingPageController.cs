﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EPiServer.Logging;
using EPiServer.Reference.Commerce.Site.B2B.Filters;
using EPiServer.Reference.Commerce.Site.B2B.Models.ViewModels;
using EPiServer.Reference.Commerce.Site.B2B.ServiceContracts;
using EPiServer.Reference.Commerce.Site.Features.Budgeting.Pages;
using EPiServer.Reference.Commerce.Site.Features.Budgeting.ViewModels;
using EPiServer.Web.Mvc;
using Mediachase.Commerce;
using EPiServer.Reference.Commerce.Site.B2B;
using Mediachase.Commerce.Customers;

namespace EPiServer.Reference.Commerce.Site.Features.Budgeting.Controllers
{
    [Authorize]
    public class BudgetingPageController : PageController<BudgetingPage>
    {
        private readonly IBudgetService _budgetService;
        private readonly IOrganizationService _organizationService;
        private readonly ICurrentMarket _currentMarket;
        private readonly ICustomerService _customerService;

        public BudgetingPageController(IBudgetService budgetService, IOrganizationService organizationService, ICurrentMarket currentMarket, ICustomerService customerService)
        {
            _budgetService = budgetService;
            _organizationService = organizationService;
            _currentMarket = currentMarket;
            _customerService = customerService;
        }

        [NavigationAuthorize("Admin,Approver,Purchaser")]
        public ActionResult Index(BudgetingPage currentPage)
        {
           List<BudgetViewModel> organizationBudgets = new List<BudgetViewModel>();
           List<BudgetViewModel> suborganizationsBudgets = new List<BudgetViewModel>();
          
            var currentOrganization = !string.IsNullOrEmpty(Session[Constants.Fields.SelectedSuborganization]?.ToString()) 
                ? _organizationService.GetSubOrganizationById(Session[Constants.Fields.SelectedSuborganization].ToString()) 
                : _organizationService.GetCurrentUserOrganization();

            var viewModel = new BudgetingPageViewModel
            {
                CurrentPage = currentPage,
                OrganizationBudgets = organizationBudgets
            };

            viewModel.IsSubOrganization = !string.IsNullOrEmpty(Session[Constants.Fields.SelectedSuborganization]?.ToString());

            var currentBudget = _budgetService.GetCurrentOrganizationBudget(currentOrganization.OrganizationId);
            if (currentBudget != null) viewModel.CurrentBudgetViewModel = new BudgetViewModel(currentBudget);
    
            var budgets = _budgetService.GetOrganizationBudgets(currentOrganization.OrganizationId);
            if (budgets != null)
            {
                organizationBudgets.AddRange(budgets.Select(budget => new BudgetViewModel(budget) { OrganizationName = currentOrganization.Name, IsCurrentBudget = (currentBudget?.BudgetId == budget.BudgetId)}));
                viewModel.OrganizationBudgets = organizationBudgets;
            }

            var suborganizations = currentOrganization.SubOrganizations;
            if (suborganizations != null)
                foreach (var suborganization in suborganizations)
                {
                  var suborgBudgets = _budgetService.GetCurrentOrganizationBudget(suborganization.OrganizationId);
                  if (suborgBudgets != null)
                  {
                      suborganizationsBudgets.Add(new BudgetViewModel(suborgBudgets) { OrganizationName = suborganization.Name });
                  }
                  viewModel.SubOrganizationsBudgets = suborganizationsBudgets;
                }

            List<BudgetViewModel> purchasersBudgetsViewModel = new List<BudgetViewModel>();
            var purchasersBudgets = _budgetService.GetOrganizationPurchasersBudgets(currentOrganization.OrganizationId);
            if (purchasersBudgets != null)
            {
                purchasersBudgetsViewModel.AddRange(purchasersBudgets.Select(purchaserBudget => new BudgetViewModel(purchaserBudget)));
            }
            viewModel.PurchasersSpendingLimits = purchasersBudgetsViewModel;

            viewModel.IsAdmin = CustomerContext.Current.CurrentContact.Properties[Constants.Fields.UserRole].Value.ToString() == Constants.UserRoles.Admin;

            return View(viewModel);
        }

        [NavigationAuthorize("Admin")]
        public ActionResult AddBudget(BudgetingPage currentPage)
        {
            var viewModel = new BudgetingPageViewModel { CurrentPage = currentPage };
            try
            {
                if (!string.IsNullOrEmpty(Session[Constants.Fields.SelectedSuborganization]?.ToString()))
                {
                    var suborganization = _organizationService.GetSubOrganizationById(Session[Constants.Fields.SelectedSuborganization].ToString());
                    var organizationCurrentBudget = _budgetService.GetCurrentOrganizationBudget(suborganization.ParentOrganization.OrganizationId);
                    viewModel.AvailableCurrencies = new List<string> { organizationCurrentBudget.Currency };
                    viewModel.IsSubOrganization = true;
                    viewModel.NewBudgetOption = new BudgetViewModel(organizationCurrentBudget);
                }
                else
                {
                    var availableCurrencies = _currentMarket.GetCurrentMarket().Currencies as List<Currency>;
                    if (availableCurrencies != null)
                    {
                        var currencies = new List<string>();
                        currencies.AddRange(availableCurrencies.Select(currency => currency.CurrencyCode));
                        viewModel.AvailableCurrencies = currencies;
                    }
                }

            }
            catch (Exception ex)
            {
                LogManager.GetLogger(GetType()).Error(ex.Message, ex.StackTrace);
                return RedirectToAction("Index");
            }
          
            return View(viewModel);
        }

        [NavigationAuthorize("Admin")]
        public ActionResult NewBudget(DateTime startDateTime, DateTime finishDateTime, decimal amount, string currency, string status)
        {
            var success = true;
            try
            {
                var currentOrganization = _organizationService.GetCurrentUserOrganization();
                var organizationId = currentOrganization.OrganizationId;

                if (!string.IsNullOrEmpty(Session[Constants.Fields.SelectedSuborganization]?.ToString()))
                {
                    organizationId = Guid.Parse(Session[Constants.Fields.SelectedSuborganization].ToString());
                    // Validate Ammount of available budget.
                    if (!_budgetService.HasEnoughAmount(currentOrganization.OrganizationId, amount, startDateTime, finishDateTime))
                        return Json(new {success = false});
                    // It should overlap with another budget of the parent organization
                    if (!_budgetService.IsSuborganizationValidTimeSlice(startDateTime, finishDateTime, currentOrganization.OrganizationId))
                        return Json(new { success = false });
                    // Validate for existing current budget. Avoid duplicate current budget since the budgets of suborg. must fit org. date times. 
                    if (_budgetService.GetBudgetByTimeLine(organizationId,startDateTime,finishDateTime) != null)
                        return Json(new { success = false });
                    // Have to deduct from organization correpondent budget.
                    if (!_budgetService.LockOrganizationAmount(startDateTime, finishDateTime, currentOrganization.OrganizationId, amount))
                        return Json(new { success = false });
                }
                else
                {
                    // Invalid date selection. Overlaps with another budget.
                    if (!_budgetService.IsTimeOverlapped(startDateTime, finishDateTime, organizationId))
                        return Json(new { success = false });
                }

                _budgetService.CreateNewBudget(new BudgetViewModel
                {
                    Amount = amount,
                    SpentBudget = 0,
                    Currency = currency,
                    StartDate = startDateTime,
                    DueDate = finishDateTime,
                    OrganizationId = organizationId,
                    IsActive = true,
                    Status = status,
                    LockAmount = 0
                });
            }
            catch (Exception ex)
            {
                LogManager.GetLogger(GetType()).Error(ex.Message, ex.StackTrace);
                success = false;
            }
           
            return Json(new {success = success});
        }

        [NavigationAuthorize("Admin")]
        public ActionResult EditBudget(BudgetingPage currentPage, int budgetId)
        {
            var currentOrganization = !string.IsNullOrEmpty(Session[Constants.Fields.SelectedSuborganization]?.ToString()) 
                ? _organizationService.GetSubOrganizationById(Session[Constants.Fields.SelectedSuborganization].ToString()) 
                : _organizationService.GetCurrentUserOrganization();

            var currentBudget = _budgetService.GetCurrentOrganizationBudget(currentOrganization.OrganizationId);
            var viewModel = new BudgetingPageViewModel
            {
                CurrentPage = currentPage,
                NewBudgetOption = new BudgetViewModel(_budgetService.GetBudgetById(budgetId))
            };
            if (currentBudget != null && currentBudget.BudgetId == budgetId) viewModel.NewBudgetOption.IsCurrentBudget = true;

            return View(viewModel);
        }

        [NavigationAuthorize("Admin")]
        public ActionResult UpdateBudget(DateTime startDateTime, DateTime finishDateTime, decimal amount, string currency, string status, int budgetId)
        {
            var success = true;

            var currentOrganization = !string.IsNullOrEmpty(Session[Constants.Fields.SelectedSuborganization]?.ToString())
                ? _organizationService.GetSubOrganizationById(Session[Constants.Fields.SelectedSuborganization].ToString())
                : _organizationService.GetCurrentUserOrganization();
            var budget = _budgetService.GetBudgetById(budgetId);

            //Can update bugdets of same organization as request user organization
            if (budget.OrganizationId != currentOrganization.OrganizationId && currentOrganization.SubOrganizations.All(suborg => suborg.OrganizationId != budget.OrganizationId))
                return Json(new { success = false });
            // Amount cannot be lower then spent amount.
            if (budget != null && budget.SpentBudget > amount)
                return Json(new { success = false });
            try
            {
                var isSuborganizationBudget = _organizationService.GetSubOrganizationById(budget.OrganizationId.ToString()).ParentOrganization != null;

                if (!string.IsNullOrEmpty(Session[Constants.Fields.SelectedSuborganization]?.ToString()) || isSuborganizationBudget)
                {
                   currentOrganization = _organizationService.GetCurrentUserOrganization();
                   // Check budget ballance.
                   if(!_budgetService.CheckAmount(currentOrganization.OrganizationId, amount, budget.Amount))
                       return Json(new { success = false });
                   // Have to unlock the old amount and to lock the new amount from the organization correpondent budget.
                   if (!_budgetService.UnLockOrganizationAmount(startDateTime, finishDateTime, currentOrganization.OrganizationId, budget.Amount))
                   return Json(new { success = false });
                   if (!_budgetService.LockOrganizationAmount(startDateTime, finishDateTime, currentOrganization.OrganizationId, amount))
                       return Json(new { success = false });
                }

                _budgetService.UpdateBudget( new BudgetViewModel
                    {
                        Amount = amount,
                        StartDate = startDateTime,
                        DueDate = finishDateTime,
                        Status = status,
                        Currency = currency,
                        BudgetId = budgetId
                    });
            }
            catch (Exception ex)
            {
                LogManager.GetLogger(GetType()).Error(ex.Message, ex.StackTrace);
                success = false;
            }

            return Json(new { result = success });
        }
        
        [NavigationAuthorize("Admin")]
        public ActionResult AddBudgetToUser(BudgetingPage currentPage)
        {
            var viewModel = new BudgetingPageViewModel { CurrentPage = currentPage };
            var currentOrganization = !string.IsNullOrEmpty(Session[Constants.Fields.SelectedSuborganization]?.ToString())
               ? _organizationService.GetSubOrganizationById(Session[Constants.Fields.SelectedSuborganization].ToString())
               : _organizationService.GetCurrentUserOrganization();
            var budget =_budgetService.GetCurrentOrganizationBudget(currentOrganization.OrganizationId);
            if (budget == null)
            {
                return RedirectToAction("Index");
            }
            viewModel.NewBudgetOption = new BudgetViewModel(budget);

            return View(viewModel);
        }

        [NavigationAuthorize("Admin")]
        public ActionResult NewBudgetToUser(DateTime startDateTime, DateTime finishDateTime, decimal amount, string currency, string status, string userEmail)
        {
            var success = true;
            try
            {
                var currentOrganization = !string.IsNullOrEmpty(Session[Constants.Fields.SelectedSuborganization]?.ToString())
               ? _organizationService.GetSubOrganizationById(Session[Constants.Fields.SelectedSuborganization].ToString())
               : _organizationService.GetCurrentUserOrganization();

                var organizationId = currentOrganization.OrganizationId;
                var user = _customerService.GetCustomerByEmail(userEmail);

                //Check user role.
                if (user.Properties["UserRole"].Value.ToString() != Constants.UserRoles.Purchaser)
                    return Json(new { success = false });
                // Can assign only to same organization user.
                if (user.ContactOrganization.Name != currentOrganization.Name)
                    return Json(new { success = false });

                var userGuid = Guid.Parse(user.PrimaryKeyId.Value.ToString());
                // Validate Ammount of available budget.
                if (!_budgetService.HasEnoughAmount(currentOrganization.OrganizationId, amount, startDateTime, finishDateTime))
                    return Json(new { success = false });

                // It should overlap with another budget of the parent organization
                if (!_budgetService.IsSuborganizationValidTimeSlice(startDateTime, finishDateTime, organizationId))
                    return Json(new { success = false });

                // Can have only one active budget per purchaser per current period
                if (_budgetService.GetCustomerCurrentBudget(organizationId,userGuid) != null)
                    return Json(new { success = false });
                
                // Have to deduct from organization correpondent budget.
                if (!_budgetService.LockOrganizationAmount(startDateTime, finishDateTime, currentOrganization.OrganizationId, amount))
                    return Json(new { success = false });

                _budgetService.CreateNewBudget(new BudgetViewModel
                {
                    Amount = amount,
                    SpentBudget = 0,
                    Currency = currency,
                    StartDate = startDateTime,
                    DueDate = finishDateTime,
                    ContactId = userGuid,
                    OrganizationId = organizationId,
                    IsActive = true,
                    Status = status,
                    PurchaserName = user.FullName,
                    LockAmount = 0
                });
            }
            catch (Exception ex)
            {
                LogManager.GetLogger(GetType()).Error(ex.Message, ex.StackTrace);
                success = false;
            }

            return Json(new { success = success });
        }

        [NavigationAuthorize("Admin")]
        public ActionResult EditUserBudget(BudgetingPage currentPage, int budgetId)
        {
            var viewModel = new BudgetingPageViewModel
            {
                CurrentPage = currentPage,
                NewBudgetOption = new BudgetViewModel(_budgetService.GetBudgetById(budgetId))
            };
            viewModel.NewBudgetOption.IsCurrentBudget = true;
            return View(viewModel);
        }

        [NavigationAuthorize("Admin")]
        public ActionResult UpdateUserBudget(DateTime startDateTime, DateTime finishDateTime, decimal amount, string currency, string status, int budgetId)
        {
            var currentOrganization = !string.IsNullOrEmpty(Session[Constants.Fields.SelectedSuborganization]?.ToString())
              ? _organizationService.GetSubOrganizationById(Session[Constants.Fields.SelectedSuborganization].ToString())
              : _organizationService.GetCurrentUserOrganization();
            var budget = _budgetService.GetBudgetById(budgetId);

            //Can update bugdets of same organization as request user organization
            if (budget.OrganizationId != currentOrganization.OrganizationId && currentOrganization.SubOrganizations.All(suborg => suborg.OrganizationId != budget.OrganizationId))
                return Json(new { success = false });
            // Amount cannot be lower then spent amount.
            if (budget.SpentBudget > amount)
                return Json(new { success = false });
            // Check budget ballance.
            if (!_budgetService.CheckAmount(budget.OrganizationId, amount, budget.Amount))
                return Json(new { success = false });
            // Have to unlock the old amount and to lock the new amount from the organization correpondent budget.
            if (!_budgetService.UnLockOrganizationAmount(startDateTime, finishDateTime, budget.OrganizationId, budget.Amount))
                return Json(new { success = false });
            if (!_budgetService.LockOrganizationAmount(startDateTime, finishDateTime, budget.OrganizationId, amount))
                return Json(new { success = false });

            var success = true;
            try
            {
                _budgetService.UpdateBudget(new BudgetViewModel
                {
                    Amount = amount,
                    StartDate = startDateTime,
                    DueDate = finishDateTime,
                    Status = status,
                    Currency = currency,
                    BudgetId = budgetId,
                    PurchaserName = budget.PurchaserName
                });
            }
            catch (Exception ex)
            {
                LogManager.GetLogger(GetType()).Error(ex.Message, ex.StackTrace);
                success = false;
            }

            return Json(new { result = success });
        }

    }
}