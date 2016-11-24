﻿using System;
using System.Collections.Generic;
using EPiServer.Reference.Commerce.Site.B2B.Models.Entities;
using EPiServer.Reference.Commerce.Site.B2B.Models.ViewModels;

namespace EPiServer.Reference.Commerce.Site.B2B.ServiceContracts
{
    public interface IBudgetService
    {
        List<BudgetViewModel> GetAllBudgets();
        void CreateNewBudget(BudgetViewModel budgetModel);
        List<Budget> GetOrganizationBudgets(Guid organizationId);
        void UpdateBudget(BudgetViewModel budgetModel);
    }
}