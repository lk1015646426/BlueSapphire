using System.Collections.Generic;

namespace BlueSapphire.Models
{
    // ==========================================
    // 3. 数据模型: DuplicateGroup (标记为 partial)
    // ==========================================
    public partial class DuplicateGroup : List<DuplicateItem>
    {
        public string GroupName { get; set; } = "重复组";
    }
}