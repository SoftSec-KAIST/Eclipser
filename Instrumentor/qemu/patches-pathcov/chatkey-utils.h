/* List structure to handle call stack information */
struct node {
  abi_ulong data;
  struct node * next;
};

void push(abi_ulong x);
abi_ulong pop(void);
abi_ulong fetch_context(int sensitivity);

struct node * head = NULL;

void push(abi_ulong x) {
  struct node * new_node = (struct node *) malloc(sizeof(struct node));
  /* If so, we're all doomed, anyway */
  assert(new_node != NULL);

  new_node->data = x;
  new_node->next = head;
  head = new_node;
}

abi_ulong pop(void) {
  struct node * top_node;
  abi_ulong top_data;
  /* This cannot happen as long as we track call/ret correctly */
  assert(head != NULL);
  top_node = head;
  top_data = head->data;

  /* Update list*/
  head = head->next;

  /* free() and return */
  free(top_node);
  return top_data;
}

abi_ulong fetch_context(int sensitivity) {
  int ctx = 0;
  int i;
  struct node * ptr = head;

  for(i = 0; i < sensitivity && ptr != NULL ; i++) {
    ctx = (ctx << 8) ^ ptr->data;
    ptr = ptr->next;
  }

  return ctx;
}
