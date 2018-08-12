﻿using System;
using System.Threading.Tasks;

using Discord;

using Modix.Data.Models.Core;
using Modix.Data.Repositories;

namespace Modix.Services.Core
{
    /// <summary>
    /// Provides methods for managing and interacting with Discord users.
    /// </summary>
    public interface IUserService
    {
        /// <summary>
        /// Retrieves the user, if any, associated with the given Discord ID value.
        /// </summary>
        /// <param name="userId">The <see cref="IEntity{T}.Id" /> of the user to be retrieved.</param>
        /// <returns>
        /// The <see cref="IUser"/>, if any, retrieved from Discord.NET.
        /// This user may also be an <see cref="IGuildUser"/>, if the current request is associated with a particular guild.
        /// </returns>
        Task<IUser> GetUserAsync(ulong userId);

        /// <summary>
        /// Retrieves the user, if any, associated with the given Discord ID value that also belongs to a specified guild.
        /// </summary>
        /// <param name="guildId">The <see cref="IEntity{T}.Id" /> of the guild whose user is to be retrieved.</param>
        /// <param name="userId">The <see cref="IEntity{T}.Id" /> of the user to be retrieved.</param>
        /// <returns>The <see cref="IGuildUser"/>, if any, retrieved from Discord.NET.</returns>
        Task<IGuildUser> GetGuildUserAsync(ulong guildId, ulong userId);

        /// <summary>
        /// Updates information about the given user within the user tracking system of a guild.
        /// </summary>
        /// <param name="user">The user whose info is to be tracked.</param>
        /// <returns>A <see cref="Task"/> that will complete when the operation has completed.</returns>
        Task TrackUserAsync(IGuildUser user);
    }

    /// <inheritdoc />
    public class UserService : IUserService
    {
        /// <summary>
        /// Constructs a new <see cref="UserService"/> with the given injected dependencies.
        /// </summary>
        /// <param name="discordClient">The value to use for <see cref="DiscordClient"/>.</param>
        /// <param name="authorizationService">The value to use for <see cref="AuthorizationService"/>.</param>
        /// <param name="guildService">The value to use for <see cref="GuildService"/>.</param>
        /// <param name="guildUserRepository">The value to use for <see cref="GuildUserRepository"/>.</param>
        /// <exception cref="ArgumentNullException">Throws for all parameters.</exception>
        public UserService(IDiscordClient discordClient, IAuthorizationService authorizationService, IGuildService guildService, IGuildUserRepository guildUserRepository)
        {
            DiscordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
            AuthorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            GuildService = guildService ?? throw new ArgumentNullException(nameof(guildService));
            GuildUserRepository = guildUserRepository ?? throw new ArgumentNullException(nameof(guildUserRepository));
        }

        /// <inheritdoc />
        public async Task<IUser> GetUserAsync(ulong userId)
        {
            var user = (AuthorizationService.CurrentGuildId == null)
                ? await DiscordClient.GetUserAsync(userId)
                : await (await GuildService.GetGuildAsync(AuthorizationService.CurrentGuildId.Value))
                    .GetUserAsync(userId);

            if (user == null)
                throw new InvalidOperationException($"Discord user {userId} does not exist");

            if(user is IGuildUser guildUser)
                await TrackUserAsync(guildUser);

            return user;
        }

        /// <inheritdoc />
        public async Task<IGuildUser> GetGuildUserAsync(ulong guildId, ulong userId)
        {
            var guild = await GuildService.GetGuildAsync(guildId);
            if (guild == null)
                throw new InvalidOperationException($"Discord guild {guildId} does not exist");

            var user = await guild.GetUserAsync(userId);
            if (user == null)
                throw new InvalidOperationException($"Discord user {userId} does not exist");

            await TrackUserAsync(user);

            return user;
        }

        /// <inheritdoc />
        public async Task TrackUserAsync(IGuildUser user)
        {
            using (var transaction = await GuildUserRepository.BeginCreateTransactionAsync())
            {
                if(!(await GuildUserRepository.TryUpdateAsync(user.Id, user.GuildId, data =>
                {
                    // Only update properties that we were given. Updates can be triggered from several different sources, not all of which have all the user's info.
                    if (user.Username != null)
                        data.Username = user.Username;
                    if (user.DiscriminatorValue != 0)
                        data.Discriminator = user.Discriminator;
                    if ((user.Username != null) && (user.DiscriminatorValue != 0))
                        data.Nickname = user.Nickname;
                    data.LastSeen = DateTimeOffset.Now;
                })))
                {
                    await GuildUserRepository.CreateAsync(new GuildUserCreationData()
                    {
                        UserId = user.Id,
                        GuildId = user.GuildId,
                        Username = user.Username ?? "[UNKNOWN USERNAME]",
                        Discriminator = (user.DiscriminatorValue == 0) ? "????" : user.Discriminator,
                        Nickname = user.Nickname,
                        FirstSeen = DateTimeOffset.Now,
                        LastSeen = DateTimeOffset.Now
                    });
                }

                transaction.Commit();
            }
        }

        /// <summary>
        /// A <see cref="IDiscordClient"/> to be used to interact with the Discord API.
        /// </summary>
        internal protected IDiscordClient DiscordClient { get; }

        /// <summary>
        /// A <see cref="IAuthorizationService"/> to be used to interact with frontend authentication system, and perform authorization.
        /// </summary>
        internal protected IAuthorizationService AuthorizationService { get; }

        /// <summary>
        /// A <see cref="IGuildService"/> to be used to interact with Discord guild objects.
        /// </summary>
        internal protected IGuildService GuildService { get; }

        /// <summary>
        /// A <see cref="IGuildUserRepository"/> to be used to interact with user data within a datastore.
        /// </summary>
        internal protected IGuildUserRepository GuildUserRepository { get; }
    }
}
