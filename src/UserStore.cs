﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using AdaptiveSystems.AspNetIdentity.AzureTableStorage.Exceptions;
using log4net;
using Microsoft.AspNet.Identity;
using Microsoft.WindowsAzure.Storage;
using NExtensions;

namespace AdaptiveSystems.AspNetIdentity.AzureTableStorage
{
    public class UserStore<T> : IUserStore<T>, IUserPasswordStore<T>, IUserEmailStore<T>, IUserLockoutStore<T, string>, IUserLoginStore<T>, IUserRoleStore<T> 
        where T : User, new()
    {
        private readonly ILog _log = LogManager.GetLogger(typeof(UserStore<>));
        private readonly IdentityTables<T> _identityTables;

        public UserStore(string connectionString) : this(CloudStorageAccount.Parse(connectionString)) { }
        public UserStore(CloudStorageAccount storageAccount) : this(storageAccount, true) { }
        public UserStore(CloudStorageAccount storageAccount, bool createIfNotExists) : this(storageAccount, createIfNotExists, "users", "userNamesIndex", "userEmailsIndex", "userExternalLoginsIndex") { }
        public UserStore(CloudStorageAccount storageAccount, bool createIfNotExists, string usersTableName, string userNamesIndexTableName, string userEmailsIndexTableName, string userExternalLoginsIndexTableName)
        {
            _identityTables = new IdentityTables<T>(storageAccount, createIfNotExists, usersTableName, userNamesIndexTableName, userEmailsIndexTableName, userExternalLoginsIndexTableName);
        }

        public static UserStore<T> Create()
        {
            return new UserStore<T>(ConfigurationManager.ConnectionStrings["UserStore.ConnectionString"].ConnectionString);
        }

        private async Task CreateUserNameIndex(T user)
        {
            var userNameIndex = new UserNameIndex(user.UserName, user.Id);

            try
            {
                _log.DebugFormat("Creating username index for {0}", user);
                await _identityTables.InsertUserNamesIndexTableEntity(userNameIndex);
            }
            catch (StorageException ex)
            {
                if (ex.RequestInformation.HttpStatusCode == 409)
                {
                    throw new DuplicateUsernameException();
                }
                _log.Error(ex.Message, ex);
                throw;
            }
        }

        private async Task CreateEmailIndex(T user)
        {
            var emailIndex = new UserEmailIndex(user.Email, user.Id);

            try
            {
                _log.DebugFormat("Creating email index for {0}", user);
                await _identityTables.InsertUserEmailsIndexTableEntity(emailIndex);
            }
            catch (StorageException ex)
            {
                if (ex.RequestInformation.HttpStatusCode == 409)
                {
                    throw new DuplicateEmailException();
                }
                _log.Error(ex.Message, ex);
                throw;
            }
        }

        public async Task CreateAsync(T user)
        {
            user.ThrowIfNull("user");
            user.SetPartionAndRowKeys();

            await CreateUserNameIndex(user);
            await CreateEmailIndex(user);

            try
            {
                _log.DebugFormat("Creating user {0}", user);
                await _identityTables.InsertOrReplaceUserTableEntity(user);
            }
            catch (Exception ex)
            {
                _log.Error(ex.Message, ex);
                // attempt to delete the index item - needs work
                RemoveIndices(user).Wait();//cannt await in the catch of a try block so have to wait
                throw;
            }
        }

        public async Task DeleteAsync(T user)
        {
            user.ThrowIfNull("user");

            await _identityTables.DeleteUserTableEntity(user);

            await RemoveIndices(user);
        }

        public async Task<T> FindByIdAsync(string userId)
        {
            userId.ThrowIfNullOrEmpty("userId");

            _log.DebugFormat("Finding user by userId: {0}", userId);
            var result = await _identityTables.RetrieveUserAsync(userId);
            return (T) result;
        }

        public async Task<T> FindByNameAsync(string userName)
        {
            userName.ThrowIfNullOrEmpty("userName");

            _log.DebugFormat("Finding user by username: {0}", userName);
            var indexItem = await _identityTables.RetrieveUserNamesIndexAsync(new UserNameIndex(userName));

            if (indexItem == null)
            {
                return null;
            }

            return await FindByIdAsync(indexItem.UserId);
        }

        public async Task UpdateAsync(T user)
        {
            user.ThrowIfNull("user");

            _log.DebugFormat("Updating user: {0}", user);
            await _identityTables.UpdateUserTableEntity(user);
        }

        public void Dispose()
        {
            
        }

        public Task<string> GetPasswordHashAsync(T user)
        {
            user.ThrowIfNull("user");

            return Task.FromResult(user.PasswordHash);
        }

        public Task<bool> HasPasswordAsync(T user)
        {
            user.ThrowIfNull("user");

            return Task.FromResult(user.PasswordHash.HasValue());
        }

        public Task SetPasswordHashAsync(T user, string passwordHash)
        {
            user.ThrowIfNull("user");
            passwordHash.ThrowIfNullOrEmpty("passwordHash");

            user.PasswordHash = passwordHash;
            return Task.FromResult(0);
        }

        public async Task<T> FindByEmailAsync(string email)
        {
            email.ThrowIfNullOrEmpty("email");

            _log.DebugFormat("Finding user by email: {0}", email);
            var indexItem = await _identityTables.RetrieveUserEmailsIndexAsync(new UserEmailIndex(email));

            if (indexItem == null)
            {
                return null;
            }

            return await FindByIdAsync(indexItem.UserId);
        }

        public Task<string> GetEmailAsync(T user)
        {
            user.ThrowIfNull("user");

            return Task.FromResult(user.Email);
        }

        public Task<bool> GetEmailConfirmedAsync(T user)
        {
            user.ThrowIfNull("user");

            return Task.FromResult(user.EmailConfirmed);
        }

        public Task SetEmailAsync(T user, string email)
        {
            user.ThrowIfNull("user");
            email.ThrowIfNullOrEmpty("email");

            user.Email = email;
            return Task.FromResult(0);
        }

        public Task SetEmailConfirmedAsync(T user, bool confirmed)
        {
            user.ThrowIfNull("user");

            user.EmailConfirmed = confirmed;
            return Task.FromResult(0);
        }

        private async Task RemoveIndices(T user)
        {
            var userNameIndex = new UserNameIndex(user.UserName, user.Id);

            var emailIndex = new UserEmailIndex(user.Email, user.Id);

            var t1 = _identityTables.DeleteUserNamesIndexTableEntity(userNameIndex);
            var t2 = _identityTables.DeleteUserEmailsIndexTableEntity(emailIndex);

            await Task.WhenAll(t1, t2);
        }


        public Task<int> GetAccessFailedCountAsync(T user)
        {
            throw new NotImplementedException();
        }

        public Task<bool> GetLockoutEnabledAsync(T user)
        {
            user.ThrowIfNull("user");
            return Task.FromResult(user.LockoutEnabled);
        }

        public Task<DateTimeOffset> GetLockoutEndDateAsync(T user)
        {
            user.ThrowIfNull("user");
            return Task.FromResult((DateTimeOffset)DateTime.SpecifyKind(user.LockoutEndDate ?? new DateTime(1601, 1, 1), DateTimeKind.Utc));
        }

        public Task<int> IncrementAccessFailedCountAsync(T user)
        {
            user.ThrowIfNull("user");
            user.AccessFailedCount++;
            return Task.FromResult(0);
        }

        public Task ResetAccessFailedCountAsync(T user)
        {
            user.ThrowIfNull("user");
            user.AccessFailedCount = 0;
            return Task.FromResult(0);
        }

        public Task SetLockoutEnabledAsync(T user, bool enabled)
        {
            user.ThrowIfNull("user");
            user.LockoutEnabled = enabled;
            return Task.FromResult(0);
        }

        public Task SetLockoutEndDateAsync(T user, DateTimeOffset lockoutEnd)
        {
            user.ThrowIfNull("user");

            user.LockoutEndDate = lockoutEnd.UtcDateTime;
            return Task.FromResult(0);
        }

        public async Task AddLoginAsync(T user, UserLoginInfo login)
        {
            user.ThrowIfNull("user");
            login.ThrowIfNull("login");

            _log.DebugFormat("Adding external login: {0}, {1}", login.LoginProvider, login.ProviderKey);
            user.AddExternalLogin(login);
            await UpdateAsync(user);
            await CreateExternalLoginIndex(user, login);
        }

        public async Task RemoveLoginAsync(T user, UserLoginInfo login)
        {
            user.ThrowIfNull("user");
            login.ThrowIfNull("login");

            _log.DebugFormat("Removing external login: {0}, {1}", login.LoginProvider, login.ProviderKey);
            user.RemoveExternalLogin(login);
            await UpdateAsync(user);
            await _identityTables.DeleteUserExternalLoginIndexTableEntity(new UserExternalLoginIndex(login));
        }

        public Task<IList<UserLoginInfo>> GetLoginsAsync(T user)
        {
            user.ThrowIfNull("user");

            return Task.FromResult((IList<UserLoginInfo>)user.GetExternalLogins());
        }

        public async Task<T> FindAsync(UserLoginInfo login)
        {
            login.ThrowIfNull("login");

            _log.DebugFormat("Finding user by external login: {0}, {1}", login.LoginProvider, login.ProviderKey);
            var indexItem = new UserExternalLoginIndex(login);
            var index = await _identityTables.RetrieveUserExternalLoginIndexAsync(indexItem);

            _log.DebugFormat("REsult of finding user by external login: {0}", index);
            return index == null 
                ? null 
                : await FindByIdAsync(index.UserId);
        }

        private async Task CreateExternalLoginIndex(T user, UserLoginInfo login)
        {
            var loginIndex = new UserExternalLoginIndex(login, user.Id);

            try
            {
                _log.DebugFormat("Creating external login index for user: {0} [{1}, {2}]", user, login.LoginProvider, login.ProviderKey);
                await _identityTables.InsertUserExternalLoginIndexTableEntity(loginIndex);
            }
            catch (StorageException ex)
            {
                if (ex.RequestInformation.HttpStatusCode == 409)
                {
                    throw new DuplicateExternalLoginException();
                }
                _log.Error(ex.Message, ex);
                throw;
            }
        }

        public Task AddToRoleAsync(T user, string roleName)
        {
            user.ThrowIfNull("user");
            roleName.ThrowIfNullOrEmpty("roleName");

            user.AddToRole(roleName);

            return Task.FromResult(0);
        }

        public Task RemoveFromRoleAsync(T user, string roleName)
        {
            user.ThrowIfNull("user");
            roleName.ThrowIfNullOrEmpty("roleName");

            user.RemoveFromRole(roleName);

            return Task.FromResult(0);
        }

        public Task<IList<string>> GetRolesAsync(T user)
        {
            user.ThrowIfNull("user");

            return Task.FromResult((IList<string>)user.Roles.SplitByComma().ToList());
        }

        public Task<bool> IsInRoleAsync(T user, string roleName)
        {
            user.ThrowIfNull("user");
            roleName.ThrowIfNullOrEmpty("roleName");

            return Task.FromResult(user.IsInRole(roleName));
        }
    }
}
