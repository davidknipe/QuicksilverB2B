﻿using System;
using EPiServer.Reference.Commerce.Site.B2B.DomainServiceContracts;
using EPiServer.Reference.Commerce.Site.B2B.Enums;
using EPiServer.Reference.Commerce.Site.B2B.Models.ViewModels;
using EPiServer.Reference.Commerce.Site.B2B.ServiceContracts;
using EPiServer.ServiceLocation;

namespace EPiServer.Reference.Commerce.Site.B2B.Services
{
    [ServiceConfiguration(typeof(IOrganizationService), Lifecycle = ServiceInstanceScope.Singleton)]
    public class OrganizationService : IOrganizationService
    {
        private readonly IOrganizationDomainService _organizationDomainService;
        private readonly ICustomerDomainService _customerDomainService;
        private readonly IAddressService _addressService;

        public OrganizationService(IOrganizationDomainService organizationDomainService,
            ICustomerDomainService customerDomainService, IAddressService addressService)
        {
            _organizationDomainService = organizationDomainService;
            _customerDomainService = customerDomainService;
            _addressService = addressService;
        }

        public OrganizationModel GetCurrentUserOrganization()
        {
            var currentOrganization = _organizationDomainService.GetCurrentUserOrganizationEntity();
            if (currentOrganization == null) return null;

            if (currentOrganization.ParentOrganizationId == Guid.Empty) return new OrganizationModel(currentOrganization);

            var parentOrganization =
                _organizationDomainService.GetOrganizationEntityById(currentOrganization.ParentOrganizationId.ToString());
            return new OrganizationModel(currentOrganization)
            {
                ParentOrganization = new OrganizationModel(parentOrganization)
            };
        }

        public void CreateOrganization(OrganizationModel organizationInfo)
        {
            var organization = _organizationDomainService.GetNewOrganization();
            organization.Name = organizationInfo.Name;
            organization.SaveChanges();

            _customerDomainService.AddContactToOrganization(organization, _customerDomainService.GetCurrentContact(),
                B2BUserRoles.Admin);
            _addressService.UpdateOrganizationAddress(organization, organizationInfo.Address);
        }

        public void UpdateOrganization(OrganizationModel organizationInfo)
        {
            var organization =
                _organizationDomainService.GetOrganizationEntityById(organizationInfo.OrganizationId.ToString());
            organization.Name = organizationInfo.Name;
            organization.SaveChanges();
            _addressService.UpdateOrganizationAddress(organization, organizationInfo.Address);
        }
    }
}
