using System.Collections.Generic;
using System.Threading.Tasks;

namespace XivVoices.Engine
{
    public class Framework
    {
        public Queue<XivMessage> Queue { get; set; } = new Queue<XivMessage>();

        public Framework()
        {
        }

        public void Dispose()
        {
        }

        internal async Task Process(XivMessage xivMessage)
        {
        }


        public async Task Run(string directoryPath, bool once = false)
        {
        }
    }
}