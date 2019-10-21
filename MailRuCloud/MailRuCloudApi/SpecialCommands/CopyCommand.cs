using System.Collections.Generic;
using System.Threading.Tasks;
using YaR.MailRuCloud.Api.Base;

namespace YaR.MailRuCloud.Api.SpecialCommands
{
    public class CopyCommand : SpecialCommand
    {
        public CopyCommand(MailRuCloud cloud, string path, IList<string> parames) : base(cloud, path, parames)
        {
        }

        protected override MinMax<int> MinMaxParamsCount { get; } = new MinMax<int>(1, 2);

        public override async Task<SpecialCommandResult> Execute()
        {
            string source = WebDavPath.Clean(Parames.Count == 1 ? Path : Parames[0]);
            string target = WebDavPath.Clean(Parames.Count == 1 ? Parames[0] : Parames[1]);

            var res = await Cloud.Copy(source, target);
            return new SpecialCommandResult(res);

        }
    }
}