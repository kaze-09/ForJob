#include<iostream>
using namespace std;
class Solution {
public:
    int numberOfSets(int n, int k) {
        int res = 0;
        auto dfs = [&](auto&& dfs, int start, int cur)
            {
                if (cur == k)
                {
                    ++res;
                    return;
                }
                for (int i = start; i < n; i++)
                {
                    if (i - start >= 1)
                    {
                        //cout << "cur:" << cur << "start:" << start << endl;
                        ++cur;
                        dfs(dfs, i, cur);
                        --cur;
                    }

                }
            };
        dfs(dfs, 0, 0);
        return res;
    }
};

//int main()
//{
//    Solution sol;
//    sol.numberOfSets(4, 2);
//}