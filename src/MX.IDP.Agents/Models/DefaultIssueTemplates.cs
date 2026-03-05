namespace MX.IDP.Agents.Models;

/// <summary>
/// Default issue templates per campaign source type.
/// Used when no campaign-level override is provided.
/// </summary>
public static class DefaultIssueTemplates
{
    public static CampaignIssueTemplate GetForSourceType(string sourceType) => sourceType switch
    {
        "advisor" => Advisor,
        "policy" => Policy,
        "dependabot" => Dependabot,
        "codeql" => CodeQL,
        "dev_standards" => DevStandards,
        "repo_config" => RepoConfig,
        "kql" => Kql,
        _ => Default
    };

    public static readonly CampaignIssueTemplate Default = new()
    {
        TitlePattern = "[IDP] {severity}: {title}",
        BodyTemplate = """
            ## {title}

            **Severity:** {severity}
            **Source:** {sourceType}
            **Campaign:** {campaignName}

            ### Details

            {description}

            ---
            _Automatically created by IDP Campaign **{campaignName}**_
            """,
        Labels = ["idp-campaign"]
    };

    public static readonly CampaignIssueTemplate Advisor = new()
    {
        TitlePattern = "[IDP] {severity} Advisor: {title}",
        BodyTemplate = """
            ## Azure Advisor Recommendation

            **Severity:** {severity}
            **Category:** {sourceType}
            **Resource:** `{resourceId}`

            ### Recommendation

            {description}

            ### Action Required

            Review the Azure Advisor recommendation and apply the suggested changes. See the [Azure Portal](https://portal.azure.com/#blade/Microsoft_Azure_Expert/AdvisorMenuBlade/overview) for full details.

            ---
            _Automatically created by IDP Campaign **{campaignName}**_
            """,
        Labels = ["idp-campaign", "azure-advisor"]
    };

    public static readonly CampaignIssueTemplate Policy = new()
    {
        TitlePattern = "[IDP] Policy Violation: {title}",
        BodyTemplate = """
            ## Azure Policy Non-Compliance

            **Severity:** {severity}
            **Resource:** `{resourceId}`

            ### Policy Violation

            {description}

            ### Remediation

            Review the non-compliant resource and bring it into compliance with the organisational policy. Consider using Azure Policy remediation tasks where available.

            ---
            _Automatically created by IDP Campaign **{campaignName}**_
            """,
        Labels = ["idp-campaign", "azure-policy", "compliance"]
    };

    public static readonly CampaignIssueTemplate Dependabot = new()
    {
        TitlePattern = "[IDP] {severity} Dependency Alert: {title}",
        BodyTemplate = """
            ## Dependabot Security Alert

            **Severity:** {severity}

            ### Vulnerability Details

            {description}

            ### Remediation

            Update the affected dependency to the recommended version. Run your test suite after upgrading to verify compatibility.

            ---
            _Automatically created by IDP Campaign **{campaignName}**_
            """,
        Labels = ["idp-campaign", "dependabot", "security"]
    };

    public static readonly CampaignIssueTemplate CodeQL = new()
    {
        TitlePattern = "[IDP] {severity} CodeQL: {title}",
        BodyTemplate = """
            ## CodeQL Security Finding

            **Severity:** {severity}

            ### Finding Details

            {description}

            ### Remediation

            Review the flagged code and apply the recommended fix. Refer to the CodeQL documentation for guidance on this rule.

            ---
            _Automatically created by IDP Campaign **{campaignName}**_
            """,
        Labels = ["idp-campaign", "codeql", "security"]
    };

    public static readonly CampaignIssueTemplate DevStandards = new()
    {
        TitlePattern = "[IDP] Standards: {title}",
        BodyTemplate = """
            ## Development Standards Check

            **Severity:** {severity}

            ### Issue

            {description}

            ### Expected Standard

            Ensure the repository follows organisational development standards including branch protection, required status checks, and code review policies.

            ---
            _Automatically created by IDP Campaign **{campaignName}**_
            """,
        Labels = ["idp-campaign", "dev-standards", "compliance"]
    };

    public static readonly CampaignIssueTemplate RepoConfig = new()
    {
        TitlePattern = "[IDP] Repo Config: {title}",
        BodyTemplate = """
            ## Repository Configuration Issue

            **Severity:** {severity}

            ### Issue

            {description}

            ### Recommended Action

            Update the repository settings to match organisational standards for descriptions, topics, default branch naming, and merge policies.

            ---
            _Automatically created by IDP Campaign **{campaignName}**_
            """,
        Labels = ["idp-campaign", "repo-config", "hygiene"]
    };

    public static readonly CampaignIssueTemplate Kql = new()
    {
        TitlePattern = "[IDP] {severity}: {title}",
        BodyTemplate = """
            ## Resource Graph Finding

            **Severity:** {severity}
            **Resource:** `{resourceId}`

            ### Details

            {description}

            ### Action Required

            Review the identified resource and take appropriate action based on the finding details.

            ---
            _Automatically created by IDP Campaign **{campaignName}**_
            """,
        Labels = ["idp-campaign", "resource-graph"]
    };
}
