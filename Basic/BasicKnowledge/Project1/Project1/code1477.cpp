#include<vector>
#include<queue>
using namespace std;
class Solution {
public:
    int minSumOfLengths(vector<int>& arr, int target) {
        int n = arr.size();
        int l = 0, r = 0;
        int sum = 0;
        priority_queue<int, vector<int>, greater<int>>pq;
        while (r < n)
        {
            sum += arr[r];
            if (sum == target)
            {
                pq.push(r - l + 1);
                ++r;
                l = r;
            }
            else if (sum > target)
            {
                while (sum > target)
                {
                    sum -= arr[l];
                    ++l;
                }
                if (sum == target)pq.push(r - l + 1);
                ++r;
            }
            else
                ++r;
        }
        if (pq.size() >= 2)
        {
            int res = pq.top();
            pq.pop();
            res += pq.top();
            return res;
        }
        else return -1;
    }
};
//int main()
//{
//    Solution sol;
//    vector<int> arr = { 3, 2, 2, 4, 3 };
//    sol.minSumOfLengths(arr, 3);
//}