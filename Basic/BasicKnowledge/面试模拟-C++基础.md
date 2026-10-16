# 游戏客户端面试模拟：C++ 基础

已问到第 20 题。第 21 题已出，尚未作答。

## 1. 源码到进程

一段 C++ 从源码到跑起来，经过哪几步？

预处理 → 编译 → 汇编 → 链接 → 加载运行。

- **预处理**：文本替换。展开 `#include`、展开宏、处理 `#ifdef`、删注释。不做语法检查。
- **编译**：语法和类型检查，翻译成汇编。
- **汇编**：汇编变成目标文件 `.o`（Windows 上常是 `.obj`）。里面是机器码、符号表、重定位信息，还不是完整程序。
- **链接**：把多个 `.o` 和库拼成可执行文件。
  - **解析符号**：给「我要用谁」找到唯一的定义。找不到是 `undefined reference`，找到两份是 `multiple definition`。
  - **重定位**：按最终布局把占位地址填上。填的是**虚拟地址**，不是物理地址。
- **运行**：操作系统建立进程，把程序映像映射进虚拟地址空间，从 `main` 进入。ASLR 可能再把基址整体挪一次。

**映像**是链接后排好的二进制布局：磁盘上的 exe，以及加载进进程后的内存映像。静态链接把库代码写进这份映像，不是改源文件。

## 2. 静态链接和动态链接

差别在于库代码是写进最终程序，还是运行时再绑。

| | 静态链接 | 动态链接 |
| --- | --- | --- |
| 库代码 | 复制进最终映像 | 留在 dll/so，运行时映射进进程 |
| 体积 | exe 大 | exe 小，要带齐 dll |
| 更新 | 重新链接、整包发布 | 可以只换 dll |
| 启动 | 没有解析库的开销 | 加载并绑定符号 |

动态链接在链接期只记录「要用哪个库的哪个符号」。运行时由装载器把 dll 映射进来再填地址。可以启动时全部绑定，也可以第一次调用才绑定。

游戏客户端里，系统 API 和引擎、SDK 几乎都是动态链接。自己的玩法代码可以静态链进主程序。

## 3. 进程地址空间

| 区域 | 放什么 | 生命周期 | 谁分配/释放 |
| --- | --- | --- | --- |
| 代码区 | 指令，通常只读 | 整个程序 | 系统装入 |
| 常量区 | 字符串字面量等 | 整个程序 | 编译期确定 |
| 全局/静态区 | 全局变量、`static` 变量（含函数内 `static` 局部） | 整个程序 | 启动时初始化，结束时释放 |
| 栈 | 局部变量、函数参数、返回地址 | 函数调用期间 | 调用时入栈，返回时弹出 |
| 堆 | `new` / `malloc` 得到的内存 | 直到释放 | 程序员或智能指针 |

对象可以在栈上。`Player p;` 是栈上的对象；`new Player` 才是堆上的对象。局部变量默认在栈上。选栈还是堆，看寿命和大小：函数里用完就扔的放栈；要活过当前函数、或体积很大的放堆。

栈大约 1MB 量级，大数组和深递归会爆栈。函数内的 `static` 局部变量不在栈上。

## 4. 指针变量和它指向的对象

```cpp
int a = 1;
int* p = &a;
```

`a` 和 `p` 都在栈上。`*p` 不是第三块内存，就是 `a`。

指针里存的是地址，可以指向栈、堆或全局区。

```cpp
int* p1 = &a;            // 指向栈
int* p2 = new int(1);    // 指针在栈，指向堆
int* p3 = &g;            // 指向全局区
```

## 5. 指针和引用

指针是一个对象，里面存着另一个对象的地址。引用是别名，不是对象。

```cpp
int a = 1, b = 2;
int& r = a;   // 绑定死了
r = b;        // 赋值：a 变成 2，r 仍然绑着 a
```

| | 指针 | 引用 |
| --- | --- | --- |
| 能否为空 | 可以是 `nullptr` | 必须绑到已有对象 |
| 能否改指向 | `p = &b` 可以 | 不能改绑 |
| 使用 | `*p`、`p->` | 直接当原对象用 |
| 自己的地址 | `&p` 是指针自己的地址 | `&r` 就是被绑定对象的地址 |

只读大对象用 `const T&`。需要表示「可能没有」时用指针。

## 6. 内存对齐

数据的起始地址要是它对齐值的倍数，CPU 才能按字长或 cache line 读。不够就填 **padding**（填充字节）。

1. 每个成员的起始偏移必须是该成员对齐值的倍数，不够就在前面填 padding。
2. 结构体自己的对齐值等于成员对齐值的最大值。
3. 结构体总大小必须是自身对齐值的倍数，不够就在尾部再填。

基本类型的对齐值常常等于自身大小。调成员顺序能变小，是因为小成员靠在一起时，少垫几次。`#pragma pack(1)` 可以压紧，访问可能变慢。

## 7. 空类和虚函数指针

空类 `sizeof` 是 1，是为了让每个对象都有独立地址，能放进数组。C++ 不允许完整对象大小为 0。

有虚函数后，对象里会放一个 **vptr**，指向该类的虚表。64 位下指针是 8 字节，空类加上一个虚函数后 `sizeof` 一般是 8。成员函数本身在代码区，不占对象空间。

## 8. new/delete 和 malloc/free

`new` / `delete` 是关键字，`malloc` / `free` 是库函数。不能混用。

- `new`：先 `operator new` 拿生内存，再构造。
- `delete`：先析构，再 `operator delete` 还内存。
- `malloc` 失败返回 `nullptr`；`new` 默认抛 `std::bad_alloc`。
- `operator new` 可以重载，不保证永远调用 `malloc`。

`new T[n]` 要配 `delete[]`。`delete[]` 会先读出元素个数，析构 n 个对象，再按正确起始地址释放。写成 `delete` 是未定义行为。

配对：`new` 配 `delete`，`new[]` 配 `delete[]`，`malloc` 配 `free`。

## 9. placement new 和对象池

`new` 的两步是分配和构造。placement new 只做构造：

```cpp
char buf[sizeof(Player)];
Player* p = new (buf) Player(id);  // 不分配
p->~Player();                      // 手动析构，不能 delete p
```

子弹、粒子用对象池：事先准备好槽位，发射时 placement new，消失时手动析构并把槽位还回去，避免每发都 `new` / `delete`。

## 10. 三种智能指针

都是 RAII：智能指针析构时处理它管的资源。差别是所有权。

| | 所有权 | 拷贝 | 计数到 0 |
| --- | --- | --- | --- |
| `unique_ptr` | 独占 | 禁止，只能移动 | 析构时直接 `delete` |
| `shared_ptr` | 共享 | `use_count++` | `delete` 对象 |
| `weak_ptr` | 不拥有 | `weak_count++` | 不删对象；两边都到 0 才删控制块 |

`unique_ptr` 的设计就是独占，不是缺陷。`shared_ptr` 用于多方都要保证对象还活着。循环引用是 A 里有 `shared_ptr<B>`，B 里又有 `shared_ptr<A>`，计数到不了 0。一边改成 `weak_ptr`，用之前 `lock()`。

`unique_ptr` 不泄漏，是因为它的析构函数里有 `delete`。局部对象离开作用域、函数 `return`、异常栈展开、作为成员被外层带着析构，都会调用析构函数。

析构时机：

| 对象在哪 | 何时析构 |
| --- | --- |
| 局部变量 | 离开作用域 |
| 类成员 | 外层对象析构时，按声明反序 |
| `new` 出来的对象 | `delete` 时，先析构再还内存 |
| 临时对象 | 整句表达式结束 |
| 全局、函数内 `static` | 程序结束 |
| 容器元素 | 被删掉，或容器析构 |
| placement new | 编译器不自动析构，要自己调用 |

用法：

```cpp
auto bullet = std::make_unique<Bullet>(pos);   // 类型是 std::unique_ptr<Bullet>
auto other = std::move(bullet);                // 所有权转走，bullet 变空

auto player = std::make_shared<Player>(id);    // 类型是 std::shared_ptr<Player>
auto ui = player;                              // 拷贝智能指针，use_count == 2
std::weak_ptr<Player> cache = player;
if (auto locked = cache.lock()) locked->Draw();
```

`player` 和 `ui` 若是局部变量，各自在栈上占一个 `shared_ptr`（通常两个指针，约 16 字节）。堆上只有一份 `Player` 加一块控制块。

`make_shared` 一次分配控制块和对象，在指定地址上 placement new：

```cpp
void* mem = ::operator new(offset + sizeof(Player));
ControlBlock* ctrl = new (mem) ControlBlock{1, 0};
Player* obj = new (static_cast<char*>(mem) + offset) Player(id);
```

堆分配发生在 `::operator new` 和两次 placement new。最后这行只是把已经拿到的两个地址抄进 `shared_ptr`，不再向堆要内存：

```cpp
return shared_ptr<Player>(obj, ctrl);   // 构造智能指针，保存 obj 和 ctrl
```

这是教学写法。标准库没有对外公开「直接收下现成控制块」的构造函数。业务代码用 `std::make_shared<Player>(id)`。写成 `std::shared_ptr<Player>(new Player(id))` 时，公开构造函数会再分配一块控制块，堆分配变成两次。

`shared_ptr` 自己放在哪，跟普通变量一样：局部在栈，成员跟着外层对象，全局在静态区。它指向的 `Player` 在堆上，是因为 `make_shared` 做了 `operator new`。

### 循环引用

两个对象各自用 `shared_ptr` 握住对方，`use_count` 就到不了 0。

```cpp
struct Node {
    std::shared_ptr<Node> other;
    ~Node() { std::cout << "dead\n"; }
};

void Leak() {
    auto a = std::make_shared<Node>();  // a.use_count == 1
    auto b = std::make_shared<Node>();  // b.use_count == 1
    a->other = b;                       // b.use_count == 2
    b->other = a;                       // a.use_count == 2
}   // 局部变量析构后，两边 use_count 都只减到 1，~Node 不会被调用
```

离开函数后，堆上的 A 还被 B 握着，B 还被 A 握着。一边改成 `weak_ptr`，这一边不再增加对方的 `use_count`：

```cpp
struct Node {
    std::shared_ptr<Node> next;  // 拥有下一个
    std::weak_ptr<Node>   prev;  // 回指，不拥有
};

void NoLeak() {
    auto a = std::make_shared<Node>();
    auto b = std::make_shared<Node>();
    a->next = b;   // b.use_count == 2
    b->prev = a;   // 只增加 weak_count，a.use_count 仍是 1
    if (auto prev = b->prev.lock()) {
        prev->next.reset();    // 对象还在才拿得到 shared_ptr
    }
}   // a 的 use_count 到 0，A 析构并放掉 next；接着 B 也被析构
```

### `.` 和 `->`

`.` 用在对象身上，`->` 用在指针身上。`p->member` 就是 `(*p).member`。

```cpp
Node n;
Node* p = &n;
n.next = nullptr;        // 对象，用 .
p->next = nullptr;       // 指针，用 ->
(*p).next = nullptr;     // 和上一行相同
```

引用是别名，当对象用，所以用 `.`。

`a` 的类型是 `std::shared_ptr<Node>`，`next` 是 `Node` 的成员。`a->next` 能写，是因为 `shared_ptr` 重载了 `operator->`：先交出里面的 `Node*`，再访问 `Node`。

```cpp
auto a = std::make_shared<Node>();
a.use_count();    // shared_ptr 自己的函数，用 .
a->next = b;      // 等价于 a.get()->next = b，用 ->
(*a).next = b;    // 同上
```

栈上的 `a` 只保存地址。`next` 和 `prev` 在堆上那个 `Node` 里。

### 拷贝不会 double free

```cpp
void test() {
    auto a = std::make_shared<Node>();  // use_count == 1
    auto b = a;                         // use_count == 2
}   // 先析构 b，use_count 变成 1，Node 还在
    // 再析构 a，use_count 变成 0，这时才 delete 一次
```

`b = a` 拷贝的是智能指针，两份共用一个 `use_count`。析构只做减计数，减到 0 才释放对象。前面的 `Buf` 会 double free，是因为两个裸指针的析构都无条件 `delete`，没有计数。

## 11. 热路径上的 shared_ptr

每帧大量更新的子弹、粒子不适合到处用 `shared_ptr`。拷贝和销毁都要原子改 `use_count`，还有控制块和堆上零散分配，遍历时缓存也不友好。子弹的所有权通常是单一的。

常见做法是连续数组，或对象池：

```cpp
std::vector<Bullet> bullets;
bullets.reserve(上限);
bullets.emplace_back(pos);
// 死亡时和末尾交换再 pop_back，内存留着复用
```

## 12. 四种内存错误

**野指针**：还没指向合法对象就使用，典型是未初始化。

```cpp
int* p;
*p = 1;
```

**悬空引用 / 悬空指针**：原来指着活对象，对象已经没了，名字还在。

```cpp
int& Foo() {
    int a = 1;
    return a;          // 返回后 a 已析构
}
int* p = new int(1);
delete p;
*p = 2;
```

**double free**：同一块堆内存释放两次。

```cpp
int* p = new int(1);
delete p;
delete p;
```

**浅拷贝导致的 double free**：默认拷贝只抄指针地址，两个对象析构时 `delete` 同一块。

```cpp
struct Buf {
    int* data;
    Buf() : data(new int(1)) {}
    ~Buf() { delete data; }
};
void f() {
    Buf a;       // a 在栈上，a.data 指向堆
    Buf b = a;   // b.data 和 a.data 是同一个地址
}                // 析构两次，double free
```

函数里的 `a`、`b` 是栈上的 `Buf`。`new int` 的那个 `int` 在堆上。写在函数外则 `a`、`b` 在全局区，堆上那块 `int` 仍会被释放两次。

## 13. 声明、定义、extern

- **声明**：告诉编译器名字和类型，可以多次，不分配存储。
- **定义**：分配存储，或给出函数体。同一个实体整个程序只能有一份定义。

`extern` 表示这里只有声明。用到它的 `.o` 里会留下「地址以后再填」。链接器找到唯一的定义，把虚拟地址填回去。

```cpp
// a.cpp
int g_hp = 100;       // 定义

// a.h
extern int g_hp;      // 声明

// b.cpp
void Hurt() { g_hp -= 10; }
```

- **`undefined reference`**：声明过，编译通过了，链接时找不到定义。
- **`multiple definition`**：同名实体有两份定义。
- 没声明就使用：编译期报未声明，不是 `undefined reference`。

## 14. static 的三种写法

`static` 在三个位置上改的是三件不同的事。

**局部变量前：只初始化一次，活到程序结束，名字仍只在这个函数里能用。** 它在静态区，不在栈上。普通局部变量每次调用都重新构造，函数返回就析构。

```cpp
void Foo() {
    static int n = 0;   // 第一次进入才初始化
    ++n;                // 之后每次都是上次留下的值
}
```

**文件里的全局变量前：内部链接。** 不加 `static` 的全局变量也在静态区，也活整个程序。加上之后，这个名字只在当前 `.cpp` 可见，别的文件不能 `extern` 过来。

```cpp
static int g_hp = 100;   // 只属于这个 cpp
int g_mp = 50;           // 别的 cpp 可以 extern int g_mp;
```

**类成员：属于类，不属于某一个对象。** 所有对象共用一份，不占每个对象的 `sizeof`。访问写成 `ClassName::member`。静态成员函数没有 `this`。

```cpp
struct Player {
    static int count;
    static int GetCount() { return count; }
};
int Player::count = 0;    // 类外定义一次（C++17 可在类内 inline）

Player::count++;
Player::GetCount();
```

## 15. const 修饰指针，以及成员函数的 const

从右往左读。`const` 靠近谁，谁就不能改。

```cpp
const int* p;         // 常量指针：指向 const int。*p 不能改，p 可以改指向
int* const p;         // 指针常量：指针本身是常量。p 不能改指向，*p 可以改
const int* const p;   // 两个都不能改
```

```cpp
int a = 1, b = 2;

const int* p1 = &a;
p1 = &b;     // 可以
*p1 = 3;     // 不可以

int* const p2 = &a;
p2 = &b;     // 不可以
*p2 = 3;     // 可以
```

- `int* const p` 是 const pointer，叫**指针常量**
- `const int* p` 是 pointer to const int，叫**常量指针**

成员函数后面的 `const` 表示不修改 `*this` 里的普通成员。`const` 对象只能调用这种函数。标了 `mutable` 的成员仍然可以改。

## 16. const 和 constexpr

`const` 是「通过这个名字不能改」，值可以到运行时才知道。`constexpr` 是「编译期就能算出来」，才能拿去当数组长度、模板参数、`case` 标签。

```cpp
int Get() { return 10; }

const int a = Get();      // 合法，运行时才有值，之后不能改
constexpr int b = Get();  // 非法，Get 不是编译期能算的

const int c = 10;
constexpr int d = 10;
int arr1[c];              // 合法。const int 用常量初始化时，可以当编译期整数
int arr2[d];              // 合法
int arr3[a];              // 非法。a 要等 Get() 跑完
```

`const int` 用常量初始化，有一条老规则，可以当编译期整数。换成 `double`、自定义类型，或者值来自普通函数，就要写 `constexpr`。

```cpp
constexpr double e = 2.718;

template<int N> struct Buf {};
Buf<d> ok;
Buf<a> bad;
```

`constexpr` 变量本身也是 `const`。`constexpr` 函数在参数是常量时编译期算完，参数要运行时才知道就留到运行时。

```cpp
constexpr int Square(int x) { return x * x; }
constexpr int n = Square(4);  // 编译期得到 16
int k = 3;
int m = Square(k);            // 运行时算
```

## 17. 传值、传引用、传指针

三种传法都有实参和形参。实参是调用时传进去的对象，形参是函数参数列表里的名字。差别在于形参和实参是不是同一份数据。

```cpp
void ByValue(Player p);    // 形参是实参的拷贝
void ByRef(Player& p);     // 形参是实参的别名
void ByPtr(Player* p);     // 形参里存着实参的地址
```

| | 形参是什么 | 改形参 | 能否为空 |
| --- | --- | --- | --- |
| 传值 `T` | 拷贝出一份 | 不影响实参 | 对象必须存在 |
| 传引用 `T&` | 实参的别名 | 会改到实参 | 必须绑到已有对象 |
| 传指针 `T*` | 一个地址 | `*p` 会改到实参 | 可以是 `nullptr` |

大对象只读时用 `const T&`：不拷贝，`const` 保证函数里不能改它。按值传会把整个对象拷进函数。传指针也能不拷贝，但调用处要写 `&obj`，而且可以是空，语义变成「可能没有、可能要改」。`int`、`float` 这种小类型拷贝便宜，直接传值。

```cpp
void Draw(const Player& p);
Player hero;
Draw(hero);
```

## 18. 重载和默认参数

重载看参数列表，不看返回值。参数个数、类型、是不是引用、是不是 `const`，都可以区分同名函数。只有返回值不同、参数相同，不算重载，因为调用处可以不接收返回值。

```cpp
void Hit(int dmg);
void Hit(float dmg);          // 重载
void Hit(const Player& p);    // 重载
int Hit(int dmg);             // 非法：只有返回值不同
```

默认参数写在声明里，调用时少写的实参由编译器填上。只写一次，定义处不要再写。必须从右往左连续缺省。

```cpp
void Spawn(int id, int count = 1, float scale = 1.f);
Spawn(10);            // count = 1, scale = 1.f
Spawn(10, 3);         // scale = 1.f

void Bad(int a = 0, int b);   // 非法：左边有默认值，右边没有
```

## 19. 对象里有什么

每个对象里占空间的是它自己的那份成员变量。成员函数在代码区，所有对象共用一份，不占对象空间。

```cpp
struct Player {
    int hp;
    float x, y;
    void Hit();    // 不在对象里
};
// 三个 Player 就是三份 hp、x、y，Hit 仍然只有一份
```

`static` 成员也不在每个对象里，不进 `sizeof`。有虚函数时，对象里多一个 **vptr**，指向这张类的虚表；虚表里才是函数指针。`p->Update()` 顺着 vptr 找到虚表，再取出 `Update` 的地址。

vptr 由编译器在构造过程中写好。基类构造时先指向基类虚表，派生类构造时再改成派生类虚表。

## 20. 三法则和五法则

编译器可能隐式生成六类特殊成员：默认构造、析构、拷贝构造、拷贝赋值、移动构造、移动赋值。

**三法则**：析构、拷贝构造、拷贝赋值里写了其中一个，另外两个也要自己写，或 `= delete`。  
**五法则**：再加移动构造和移动赋值。

类自己管一块堆内存时，编译器生成的拷贝只抄指针地址，这是浅拷贝，两个对象析构会 `delete` 同一块。深拷贝是新分配一块，再把内容抄进去。

```cpp
class Buffer {
    char* data;
    size_t size;
public:
    Buffer(const Buffer& o);              // 深拷贝
    Buffer& operator=(const Buffer& o);
    Buffer(Buffer&& o) noexcept;          // 偷指针，o.data = nullptr
    Buffer& operator=(Buffer&& o) noexcept;
    ~Buffer() { delete[] data; }
};
```

析构在对象生命周期结束时调用：离开作用域、`return`、异常展开栈、成员跟着外层对象销毁、`delete` 堆对象、程序结束时销毁全局和 `static`。

## 21. 尚未作答

浅拷贝和深拷贝，拷贝完成后两份对象的指针各指向哪里？移动语义想避免什么开销？`std::move` 会不会把数据搬走？
