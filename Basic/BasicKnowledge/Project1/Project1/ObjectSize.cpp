#include <iostream>
#include <cstddef>

using namespace std;

class Empty {};

class EmptyWithVirtual {
public:
    virtual void Update() {}
};

class HasInt {
public:
    int hp;
};

class HasIntWithVirtual {
public:
    virtual void Update() {}
    int hp;
};

class TwoVirtuals {
public:
    virtual void Update() {}
    virtual void Hurt() {}   // 再加一个虚函数，对象大小通常不变
    int hp;
};

//int main()
//{
//    cout << "本程序指针大小: " << sizeof(void*) << " 字节"
//         << (sizeof(void*) == 8 ? "  (64位)\n\n" : "  (32位)\n\n");
//
//    cout << "sizeof(Empty)              = " << sizeof(Empty) << "\n";
//    cout << "sizeof(EmptyWithVirtual)   = " << sizeof(EmptyWithVirtual) << "\n";
//    cout << "sizeof(HasInt)             = " << sizeof(HasInt) << "\n";
//    cout << "sizeof(HasIntWithVirtual)  = " << sizeof(HasIntWithVirtual) << "\n";
//    cout << "sizeof(TwoVirtuals)        = " << sizeof(TwoVirtuals) << "\n";
//
//    cout << "\nhp 在 HasInt 里的偏移            = " << offsetof(HasInt, hp) << "\n";
//    cout << "hp 在 HasIntWithVirtual 里的偏移 = " << offsetof(HasIntWithVirtual, hp) << "\n";
//    cout << "（有虚函数时 hp 往往从 8 开始，前面被 vptr 占掉）\n";
//
//    HasIntWithVirtual obj;
//    cout << "\n对象地址: " << &obj << "\n";
//    cout << "hp 地址:   " << &obj.hp << "\n";
//
//    return 0;
//}
