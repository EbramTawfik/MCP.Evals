using Microsoft.Extensions.Logging;
using MCP.Evals.Core.Exceptions;
using MCP.Evals.Core.Interfaces;
using MCP.Evals.Core.Models;
using System.Text.Json;

namespace MCP.Evals.Infrastructure.Evaluation;

/// <summary>
/// LLM-based evaluation scorer following SRP
/// Only responsible for scoring evaluation responses
/// </summary>
public class LlmEvaluationScorer : IEvaluationScorer
{
    private readonly ILanguageModel _languageModel;
    private readonly ILogger<LlmEvaluationScorer> _logger;

    private const string EvaluationSystemPrompt = """
        You are an expert evaluator assessing how well an LLM answers a given question. 
        Review the provided answer and score it from 1 to 5 in each of the following categories:
        
        Accuracy – Does the answer contain factual errors or hallucinations?
        Completeness – Does the answer fully address all parts of the question?
        Relevance – Is the information directly related to the question?
        Clarity – Is the explanation easy to understand and well-structured?
        Reasoning – Does the answer show logical thinking or provide evidence or rationale?
        
        Return your evaluation as a JSON object in the exact format:
        {
            "accuracy": 1-5,
            "completeness": 1-5,
            "relevance": 1-5,
            "clarity": 1-5,
            "reasoning": 1-5,
            "overall_comments": "A short paragraph summarizing the strengths and weaknesses of the answer."
        }
        
        Important: Return ONLY the JSON object, no additional text or formatting.
        """;

    public LlmEvaluationScorer(
        ILanguageModel languageModel,
        ILogger<LlmEvaluationScorer> logger)
    {
        _languageModel = languageModel;
        _logger = logger;
    }

    public async Task<EvaluationScore> ScoreResponseAsync(
        string prompt,
        string response,
        string? expectedResult = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Scoring response for prompt of length: {PromptLength}", prompt.Length);

        try
        {
            var evaluationPrompt = BuildEvaluationPrompt(prompt, response, expectedResult);

            var scoringResult = await _languageModel.GenerateResponseAsync(
                EvaluationSystemPrompt,
                evaluationPrompt,
                cancellationToken);

            var score = ParseEvaluationResult(scoringResult);

            _logger.LogDebug("Scoring completed with average score: {AverageScore:F2}", score.AverageScore);
            return score;
        }
        catch (Exception ex) when (ex is not LanguageModelException)
        {
            _logger.LogError(ex, "Failed to score evaluation response");
            throw new EvaluationException("scoring", "Failed to score response", ex);
        }
    }

    private static string BuildEvaluationPrompt(string prompt, string response, string? expectedResult)
    {
        var evaluationPrompt = $"""
            Here is the user input: {prompt}
            Here is the LLM's answer: {response}
            """;

        if (!string.IsNullOrEmpty(expectedResult))
        {
            evaluationPrompt += $"\nExpected result for reference: {expectedResult}";
        }

        return evaluationPrompt;
    }

    private EvaluationScore ParseEvaluationResult(string result)
    {
        try
        {
            // Clean the result - remove any markdown formatting or extra text
            var cleanedResult = CleanJsonResult(result);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };

            var scoreData = JsonSerializer.Deserialize<ScoreJsonData>(cleanedResult, jsonOptions);

            if (scoreData == null)
            {
                throw new InvalidOperationException("Failed to deserialize evaluation result");
            }

            return new EvaluationScore
            {
                Accuracy = scoreData.Accuracy,
                Completeness = scoreData.Completeness,
                Relevance = scoreData.Relevance,
                Clarity = scoreData.Clarity,
                Reasoning = scoreData.Reasoning,
                OverallComments = scoreData.OverallComments ?? "No comments provided"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse evaluation result, using fallback scoring. Result: {Result}", result);

            // Fallback scoring if JSON parsing fails
            return new EvaluationScore
            {
                Accuracy = 3,
                Completeness = 3,
                Relevance = 3,
                Clarity = 3,
                Reasoning = 3,
                OverallComments = $"Failed to parse evaluation result. Raw result: {result.Substring(0, Math.Min(200, result.Length))}"
            };
        }
    }

    private static string CleanJsonResult(string result)
    {
        // Remove markdown code blocks
        result = result.Replace("```json", "").Replace("```", "").Trim();

        // Find the JSON object boundaries
        var startIndex = result.IndexOf('{');
        var lastIndex = result.LastIndexOf('}');

        if (startIndex >= 0 && lastIndex > startIndex)
        {
            return result.Substring(startIndex, lastIndex - startIndex + 1);
        }

        return result;
    }

    private class ScoreJsonData
    {
        public int Accuracy { get; set; }
        public int Completeness { get; set; }
        public int Relevance { get; set; }
        public int Clarity { get; set; }
        public int Reasoning { get; set; }
        public string? OverallComments { get; set; }
    }
}