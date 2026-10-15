#include <iostream>
#include <utility>

using namespace std;

// ---------- 用来观察构造 / 析构 ----------
struct Player {
    int id;
    explicit Player(int i) : id(i) {
        cout << "Player(" << id << ") 构造\n";
    }
    ~Player() {
        cout << "Player(" << id << ") 析构\n";
    }
    void Hello() const {
        cout << "Hello from Player " << id << "\n";
    }
};

// ---------- 简易 unique_ptr：独占所有权 ----------
template<typename T>
class UniquePtr {
    T* ptr;
public:
    explicit UniquePtr(T* p = nullptr) : ptr(p) {}

    ~UniquePtr() {
        delete ptr;
    }

    UniquePtr(const UniquePtr&) = delete;
    UniquePtr& operator=(const UniquePtr&) = delete;

    UniquePtr(UniquePtr&& other) noexcept : ptr(other.ptr) {
        other.ptr = nullptr;   // 所有权被偷走，源指针必须掏空，否则析构会 double free
    }

    UniquePtr& operator=(UniquePtr&& other) noexcept {
        if (this != &other) {
            delete ptr;        // 丢掉自己原来管的对象
            ptr = other.ptr;
            other.ptr = nullptr;
        }
        return *this;
    }

    T& operator*()  const { return *ptr; }
    T* operator->() const { return ptr; }
    T* get()        const { return ptr; }

    explicit operator bool() const { return ptr != nullptr; }

    T* release() {             // 交还裸指针，自己不再负责 delete
        T* tmp = ptr;
        ptr = nullptr;
        return tmp;
    }

    void reset(T* p = nullptr) {
        if (ptr != p) {
            delete ptr;
            ptr = p;
        }
    }
};

// ---------- 简易 shared_ptr：引用计数（面试版，未做线程安全 / weak_ptr） ----------
template<typename T>
class SharedPtr {
    T*   ptr   = nullptr;
    int* count = nullptr;      // 控制块极度简化：就一个 int

    void addRef() {
        if (count) ++*count;
    }

    void decRef() {
        if (!count) return;
        if (--*count == 0) {
            delete ptr;
            delete count;
        }
        ptr = nullptr;
        count = nullptr;
    }

public:
    explicit SharedPtr(T* p = nullptr)
        : ptr(p), count(p ? new int(1) : nullptr) {}

    SharedPtr(const SharedPtr& other) : ptr(other.ptr), count(other.count) {
        addRef();
    }

    SharedPtr(SharedPtr&& other) noexcept : ptr(other.ptr), count(other.count) {
        other.ptr = nullptr;
        other.count = nullptr;
    }

    SharedPtr& operator=(const SharedPtr& other) {
        if (this != &other) {
            decRef();
            ptr = other.ptr;
            count = other.count;
            addRef();
        }
        return *this;
    }

    SharedPtr& operator=(SharedPtr&& other) noexcept {
        if (this != &other) {
            decRef();
            ptr = other.ptr;
            count = other.count;
            other.ptr = nullptr;
            other.count = nullptr;
        }
        return *this;
    }

    ~SharedPtr() { decRef(); }

    T& operator*()  const { return *ptr; }
    T* operator->() const { return ptr; }
    T* get()        const { return ptr; }
    int use_count() const { return count ? *count : 0; }
};

int main()
{
    cout << "===== UniquePtr =====\n";
    {
        UniquePtr<Player> a(new Player(1));
        a->Hello();

        UniquePtr<Player> b = std::move(a);   // 所有权转给 b，a 变空
        cout << "move 之后 a 是否为空: " << (a ? "否" : "是") << "\n";
        b->Hello();

        // UniquePtr<Player> c = b;           // 编译错误：不能拷贝
    }   // 离开作用域，b 析构 → Player(1) 析构

    cout << "\n===== SharedPtr =====\n";
    {
        SharedPtr<Player> a(new Player(2));
        cout << "a.use_count = " << a.use_count() << "\n";

        SharedPtr<Player> b = a;              // 拷贝，计数 2
        cout << "拷贝后 a.use_count = " << a.use_count()
             << ", b.use_count = " << b.use_count() << "\n";
        b->Hello();
    }   // b、a 依次析构，计数到 0 才真正 delete Player(2)

    cout << "\n程序结束\n";
    return 0;
}
