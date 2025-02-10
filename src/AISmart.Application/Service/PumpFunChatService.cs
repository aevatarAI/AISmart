using System;
using System.Threading.Tasks;
using AISmart.Agent;
using AISmart.Agent.GEvents;
using AISmart.Agents;
using AISmart.Agents.Group;
using AISmart.Application.Grains.Agents.Group;
using AISmart.Common;
using AISmart.CQRS.Provider;
using AISmart.Dto;
using AISmart.Events;
using AISmart.GAgent.Autogen;
using AISmart.GAgent.Dto;
using AISmart.Grains;
using AISmart.PumpFun;
using AISmart.Sender;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans;
using Volo.Abp.Application.Services;

namespace AISmart.Service;

public class PumpFunChatService : ApplicationService, IPumpFunChatService
{
    private readonly IClusterClient _clusterClient;
    private readonly ICqrsService _cqrsService;
    private readonly ILogger<PumpFunChatService> _logger;

    public PumpFunChatService(IClusterClient clusterClient, ICqrsService cqrsService,
        ILogger<PumpFunChatService> logger)
    {
        _clusterClient = clusterClient;
        _cqrsService = cqrsService;
        _logger = logger;
    }

    public async Task ReceiveMessagesAsync(PumpFunInputDto inputDto)
    {
        _logger.LogInformation("ReceiveMessagesAsync agentId:" + inputDto.AgentId);
        if (inputDto is { RequestMessage: not null, AgentId: not null })
        {
            var groupAgent = _clusterClient.GetGrain<IStateGAgent<GroupAgentState>>(Guid.Parse(inputDto.AgentId));
            await groupAgent.ActivateAsync();

            var publishingAgent = _clusterClient.GetGrain<IPublishingGAgent>(Guid.NewGuid());
            await publishingAgent.RegisterAsync(groupAgent);

            await publishingAgent.PublishEventAsync(new PumpFunReceiveMessageEvent
            {
                ReplyId = inputDto.ReplyId,
                RequestMessage = inputDto.RequestMessage,
            });
        }
    }

    public async Task<string> SetGroupsAsync(string chatId, string bio)
    {
        _logger.LogInformation("SetGroupsAsync, chatId:{chatId}", chatId);
        Guid groupAgentId = GuidUtil.StringToGuid(chatId);
        var groupAgent = _clusterClient.GetGrain<IStateGAgent<GroupAgentState>>(groupAgentId);

        var pumpFunGAgent = _clusterClient.GetGrain<IPumpFunGAgent>(groupAgentId);

        await pumpFunGAgent.SetPumpFunConfig(chatId);

        var pumpFunChatAgent = _clusterClient.GetGrain<IPumpFunChatGrain>(groupAgentId);
        await pumpFunChatAgent.SetAgentWithLLm(chatId, bio, "deepseek");
        await groupAgent.RegisterAsync(pumpFunChatAgent);

        await groupAgent.RegisterAsync(pumpFunGAgent);

        return groupAgentId.ToString();
    }

    public async Task<PumFunResponseDto> SearchAnswerAsync(string replyId)
    {
        _logger.LogInformation("SearchAnswerAsync, replyId:{replyId}", replyId);
        // get PumpFunGAgentState
        var eventResult =
            await _cqrsService.QueryGEventAsync<PumpFunSendMessageGEvent, PumpFunSendMessageGEventDto>(
                "pumpfunsendmessagegeventindex", replyId);
        PumFunResponseDto answer = new PumFunResponseDto
        {
            ReplyId = eventResult.ReplyId,
            ReplyMessage = eventResult.ReplyMessage
        };
        return await Task.FromResult(answer);
    }
}