using AF.Blood.Application.Domain;
using Xunit;

namespace AF.Blood.UnitTests.Domain;

public class BloodCompatibilityTests
{
    [Fact]
    public void Universal_donor_gives_to_everyone()
    {
        foreach (var recipient in BloodGroups.All)
        {
            Assert.True(BloodCompatibility.IsCompatible(recipient, "O-"),
                $"O- must be compatible with {recipient}");
        }
    }

    [Fact]
    public void Universal_recipient_receives_from_everyone()
    {
        foreach (var donor in BloodGroups.All)
        {
            Assert.True(BloodCompatibility.IsCompatible("AB+", donor),
                $"AB+ must receive from {donor}");
        }
    }

    [Fact]
    public void Rh_negative_recipients_never_receive_rh_positive_blood()
    {
        foreach (var recipient in new[] { "O-", "A-", "B-", "AB-" })
        {
            foreach (var donor in new[] { "O+", "A+", "B+", "AB+" })
            {
                Assert.False(BloodCompatibility.IsCompatible(recipient, donor),
                    $"{recipient} must not receive {donor}");
            }
        }
    }

    [Fact]
    public void Same_group_is_always_compatible()
    {
        foreach (var group in BloodGroups.All)
        {
            Assert.True(BloodCompatibility.IsCompatible(group, group));
        }
    }

    [Fact]
    public void Rh_positive_recipients_receive_their_own_rh_negative_group()
    {
        Assert.True(BloodCompatibility.IsCompatible("O+", "O-"));
        Assert.True(BloodCompatibility.IsCompatible("A+", "A-"));
        Assert.True(BloodCompatibility.IsCompatible("B+", "B-"));
        Assert.True(BloodCompatibility.IsCompatible("AB+", "AB-"));
    }

    [Fact]
    public void Donor_side_query_returns_only_compatible_recipients()
    {
        var forODonor = BloodCompatibility.CompatibleDonorGroupsForDonor("O-");
        Assert.Equal(BloodGroups.All, forODonor);

        // Canonical (BloodGroups.All) ordering: A- can help A-, A+, AB- and AB+.
        var forADonor = BloodCompatibility.CompatibleDonorGroupsForDonor("A-");
        Assert.Equal(new[] { "A+", "A-", "AB+", "AB-" }, forADonor);

        var forBPlusDonor = BloodCompatibility.CompatibleDonorGroupsForDonor("B+");
        Assert.Equal(new[] { "B+", "AB+" }, forBPlusDonor);
    }

    [Fact]
    public void Recipient_query_is_ordered_and_excludes_incompatible_groups()
    {
        var forAPlus = BloodCompatibility.CompatibleDonorGroups("A+");
        Assert.Equal(new[] { "A+", "A-", "O+", "O-" }, forAPlus);
    }

    [Fact]
    public void Unknown_group_throws()
    {
        Assert.Throws<ArgumentException>(() => BloodCompatibility.CompatibleDonorGroups("Z+"));
        Assert.Throws<ArgumentException>(() => BloodCompatibility.CompatibleDonorGroupsForDonor("Z+"));
    }
}
